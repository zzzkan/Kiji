using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Kiji.Hosting;
using Kiji.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji;

/// <summary>
/// A typed, lazily materialized view over site content. Collections are declared on
/// <see cref="KijiBuilder"/>, transformed with <see cref="Map{TResult}"/> /
/// <see cref="WithKey"/> / ordering operators, and consumed from components via dependency
/// injection or from route mappings on <see cref="KijiApp"/>.
/// </summary>
public sealed class ContentCollection<T> : IEnumerable<T>
    where T : class
{
    private readonly ContentRuntime _runtime;
    private readonly Func<IServiceProvider, (IReadOnlyList<T> Items, IReadOnlyList<string?>? Provenance)> _load;
    private Func<T, string>? _keySelector;
    private Comparison<T>? _comparison;

    internal ContentCollection(ContentRuntime runtime, Func<IServiceProvider, IReadOnlyList<T>> load)
        : this(runtime, services => (load(services), null))
    {
    }

    private ContentCollection(
        ContentRuntime runtime,
        Func<IServiceProvider, (IReadOnlyList<T> Items, IReadOnlyList<string?>? Provenance)> load)
    {
        _runtime = runtime;
        _load = load;
        _runtime.AddRegistration(services => services.AddSingleton(this));
    }

    /// <summary>
    /// The materialized items in declared order.
    /// </summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            // Enumerating the collection during a tracked render couples the page to
            // the content set as a whole (list pages must reflect added/removed items).
            PageRenderContext.Current?.Dependencies?.MarkContentSetDependency();
            return Materialized.Items;
        }
    }

    internal bool HasKey => _keySelector is not null;

    /// <summary>
    /// Projects each item into a new collection.
    /// </summary>
    public ContentCollection<TResult> Map<TResult>(Func<T, TResult> projection)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(projection);

        // Projection is positional, so each result item inherits the provenance
        // (source file) of the item it was projected from.
        return new ContentCollection<TResult>(_runtime, services =>
        {
            var materialized = GetMaterialized(services);
            IReadOnlyList<TResult> items = [.. materialized.Items.Select(projection)];
            return (items, materialized.Provenance);
        });
    }

    /// <summary>
    /// Declares a unique key for each item, enabling <see cref="TryGet"/> and <see cref="GetRequired"/>.
    /// Keys are compared case-insensitively; duplicates fail materialization with an informative error.
    /// </summary>
    public ContentCollection<T> WithKey(Func<T, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        _keySelector = keySelector;
        return this;
    }

    /// <summary>
    /// Orders the materialized items ascending by the given key.
    /// </summary>
    public ContentCollection<T> OrderBy<TKey>(Func<T, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        var comparer = Comparer<TKey>.Default;
        _comparison = (left, right) => comparer.Compare(keySelector(left), keySelector(right));
        return this;
    }

    /// <summary>
    /// Orders the materialized items descending by the given key.
    /// </summary>
    public ContentCollection<T> OrderByDescending<TKey>(Func<T, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

        var comparer = Comparer<TKey>.Default;
        _comparison = (left, right) => comparer.Compare(keySelector(right), keySelector(left));
        return this;
    }

    /// <summary>
    /// Tries to find an item by its key. Requires <see cref="WithKey"/>.
    /// </summary>
    public bool TryGet(string key, [NotNullWhen(true)] out T? item)
    {
        ArgumentNullException.ThrowIfNull(key);

        var materialized = Materialized;
        if (!materialized.Index.TryGetValue(key, out item))
        {
            return false;
        }

        RecordItemDependency(materialized, key);
        return true;
    }

    /// <summary>
    /// Finds an item by its key or throws. Requires <see cref="WithKey"/>.
    /// </summary>
    public T GetRequired(string key)
    {
        return TryGet(key, out var item)
            ? item
            : throw new KeyNotFoundException($"Content item with key '{key}' was not found.");
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Returns the key of an item as declared by <see cref="WithKey"/>. Used by route
    /// mappings and site artifacts (e.g. feeds) to correlate content with generated pages.
    /// </summary>
    public string GetKey(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var keySelector = _keySelector
            ?? throw new InvalidOperationException(
                $"ContentCollection<{typeof(T).Name}> has no key selector. Call WithKey(...) to enable key-based lookups.");

        return keySelector(item);
    }

    private static void RecordItemDependency(MaterializedCollection materialized, string key)
    {
        var dependencies = PageRenderContext.Current?.Dependencies;
        if (dependencies is null)
        {
            return;
        }

        // A keyed lookup depends only on that item's source file when known;
        // untracked items fall back to the whole content set (conservative).
        if (materialized.ProvenanceByKey.TryGetValue(key, out var sourceFile) && sourceFile is not null)
        {
            dependencies.AddFile(sourceFile);
        }
        else
        {
            dependencies.MarkContentSetDependency();
        }
    }

    private MaterializedCollection Materialized => GetMaterialized(services: null);

    private MaterializedCollection GetMaterialized(IServiceProvider? services)
    {
        // The runtime resolves the provider; the parameter only exists so Map chains
        // materialize against the same provider during a single snapshot.
        _ = services;
        return _runtime.GetOrMaterialize(this, Materialize);
    }

    private MaterializedCollection Materialize(IServiceProvider services)
    {
        var (loaded, loadedProvenance) = _load(services);

        var items = loaded;
        var provenance = loadedProvenance ?? DeriveProvenance(loaded);

        if (_comparison is not null)
        {
            // Sort items and provenance together so item-level dependency tracking
            // survives ordering operators.
            var indices = Enumerable.Range(0, items.Count).ToArray();
            var comparison = _comparison;
            Array.Sort(indices, (left, right) => comparison(items[left], items[right]));

            var sortedItems = new T[items.Count];
            var sortedProvenance = new string?[items.Count];
            for (var i = 0; i < indices.Length; i++)
            {
                sortedItems[i] = items[indices[i]];
                sortedProvenance[i] = provenance[indices[i]];
            }

            items = sortedItems;
            provenance = sortedProvenance;
        }

        FrozenDictionary<string, T> index;
        FrozenDictionary<string, string?> provenanceByKey;
        if (_keySelector is null)
        {
            index = FrozenDictionary<string, T>.Empty;
            provenanceByKey = FrozenDictionary<string, string?>.Empty;
        }
        else
        {
            var indexBuilder = new Dictionary<string, T>(items.Count, StringComparer.OrdinalIgnoreCase);
            var provenanceBuilder = new Dictionary<string, string?>(items.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < items.Count; i++)
            {
                var key = _keySelector(items[i]);
                if (!indexBuilder.TryAdd(key, items[i]))
                {
                    throw new InvalidOperationException(
                        $"ContentCollection<{typeof(T).Name}> contains duplicate key '{key}'. Keys must be unique (case-insensitive).");
                }

                provenanceBuilder[key] = provenance[i];
            }

            index = indexBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            provenanceByKey = provenanceBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        return new MaterializedCollection(items, index, provenance, provenanceByKey);
    }

    private static string?[] DeriveProvenance(IReadOnlyList<T> items)
    {
        var provenance = new string?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            provenance[i] = (items[i] as IContentSourceFile)?.SourceFilePath;
        }

        return provenance;
    }

    private sealed class MaterializedCollection(
        IReadOnlyList<T> items,
        FrozenDictionary<string, T> index,
        IReadOnlyList<string?> provenance,
        FrozenDictionary<string, string?> provenanceByKey)
    {
        public IReadOnlyList<T> Items { get; } = items;

        public FrozenDictionary<string, T> Index { get; } = index;

        public IReadOnlyList<string?> Provenance { get; } = provenance;

        public FrozenDictionary<string, string?> ProvenanceByKey { get; } = provenanceByKey;
    }
}
