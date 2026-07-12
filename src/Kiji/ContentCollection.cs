using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Kiji.Hosting;
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
    private readonly Func<IServiceProvider, IReadOnlyList<T>> _load;
    private Func<T, string>? _keySelector;
    private Comparison<T>? _comparison;

    internal ContentCollection(ContentRuntime runtime, Func<IServiceProvider, IReadOnlyList<T>> load)
    {
        _runtime = runtime;
        _load = load;
        _runtime.AddRegistration(services => services.AddSingleton(this));
    }


    /// <summary>
    /// The materialized items in declared order.
    /// </summary>
    public IReadOnlyList<T> Items => Materialized.Items;

    internal bool HasKey => _keySelector is not null;

    /// <summary>
    /// Projects each item into a new collection.
    /// </summary>
    public ContentCollection<TResult> Map<TResult>(Func<T, TResult> projection)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(projection);

        return new ContentCollection<TResult>(_runtime, services => [.. GetMaterialized(services).Items.Select(projection)]);
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

        return Materialized.Index.TryGetValue(key, out item);
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
        var items = _load(services);

        if (_comparison is not null)
        {
            var sorted = new List<T>(items);
            sorted.Sort(_comparison);
            items = sorted;
        }

        FrozenDictionary<string, T> index;
        if (_keySelector is null)
        {
            index = FrozenDictionary<string, T>.Empty;
        }
        else
        {
            var builder = new Dictionary<string, T>(items.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var key = _keySelector(item);
                if (!builder.TryAdd(key, item))
                {
                    throw new InvalidOperationException(
                        $"ContentCollection<{typeof(T).Name}> contains duplicate key '{key}'. Keys must be unique (case-insensitive).");
                }
            }

            index = builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        return new MaterializedCollection(items, index);
    }

    private sealed class MaterializedCollection(IReadOnlyList<T> items, FrozenDictionary<string, T> index)
    {
        public IReadOnlyList<T> Items { get; } = items;

        public FrozenDictionary<string, T> Index { get; } = index;
    }

}
