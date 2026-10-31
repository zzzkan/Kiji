using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Kiji.Hosting;
using Kiji.Generation;
using System.Text;
using Kiji.Rendering;

namespace Kiji;

/// <summary>A lazily loaded, dependency-aware collection of site content.</summary>
/// <remarks>
/// Resolve it through dependency injection. Markdown IDs are content-directory-relative
/// paths with <c>/</c> separators, including <c>.md</c>. IDs are not page URLs.
/// </remarks>
public sealed class ContentCollection<T> : IReadOnlyCollection<T>
    where T : class
{
    private readonly string _sourceId;
    private readonly ContentRuntime _runtime;
    private readonly Func<IServiceProvider, IReadOnlyList<(string Id, T Item, string? Digest)>> _load;

    internal ContentCollection(
        ContentRuntime runtime,
        Func<IServiceProvider, IReadOnlyList<(string Id, T Item, string? Digest)>> load,
        string contentSetScope = "")
    {
        _runtime = runtime;
        _load = load;
        _sourceId = typeof(T).FullName + ":" + contentSetScope;
        runtime.Dependencies.Register(_sourceId, CollectionDigest);
        runtime.Dependencies.RegisterSource(_sourceId, key => Materialized.Index.GetValueOrDefault(key).Digest);
    }

    /// <summary>
    /// The number of items in the collection.
    /// </summary>
    public int Count => Observed.Entries.Count;

    /// <summary>
    /// The item with the given ID, compared case-insensitively.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No item carries that ID.</exception>
    public T this[string id] => TryGetValue(id, out var item)
        ? item
        : throw new KeyNotFoundException($"Content item with ID '{id}' was not found.");

    /// <summary>
    /// Finds an item by its ID, compared case-insensitively.
    /// </summary>
    public bool TryGetValue(string id, [NotNullWhen(true)] out T? item)
    {
        ArgumentNullException.ThrowIfNull(id);

        var materialized = Materialized;
        if (!materialized.Index.TryGetValue(id, out var entry))
        {
            // A miss still observed the ID set, so it is only valid while the ID set
            // holds — that is the content set, not any one file.
            MarkContentSetDependency();
            item = null;
            return false;
        }

        item = entry.Item;
        var dependencies = PageRenderContext.Current?.Dependencies;
        if (dependencies is not null)
        {
            // The lookup depends on collection membership as well as file contents:
            // a loader can filter out an unchanged file in the next snapshot.
            dependencies.AddValue(_sourceId + "/" + id.ToUpperInvariant(), entry.Digest);
        }
        return true;
    }

    /// <summary>
    /// Whether an item with the given ID exists.
    /// </summary>
    public bool Contains(string id)
    {
        return TryGetValue(id, out _);
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        return Observed.Entries.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private MaterializedContent Materialized => _runtime.GetOrMaterialize(this, Materialize);

    /// <summary>
    /// The collection as seen by a member that exposes its shape (count,
    /// enumeration). Observing the shape couples the page to the content set: a list
    /// page has to reflect items being added and removed, not just edited. ID
    /// lookups deliberately go through <see cref="Materialized"/> instead.
    /// </summary>
    private MaterializedContent Observed
    {
        get
        {
            MarkContentSetDependency();
            return Materialized;
        }
    }

    private void MarkContentSetDependency()
    {
        PageRenderContext.Current?.Dependencies?.AddValue(_sourceId, CollectionDigest());
    }

    private string? CollectionDigest()
    {
        return Materialized.CollectionDigest.Value;
    }

    private MaterializedContent Materialize(IServiceProvider services)
    {
        var loaded = _load(services);

        var index = new Dictionary<string, (T Item, string? Digest)>(loaded.Count, StringComparer.OrdinalIgnoreCase);
        var entries = new T[loaded.Count];
        for (var i = 0; i < loaded.Count; i++)
        {
            var (id, item, digest) = loaded[i];
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"The internal content loader for '{typeof(T).Name}' produced an empty ID.");
            }

            if (!index.TryAdd(id, (item, digest)))
            {
                throw new InvalidOperationException(
                    $"The internal content loader for '{typeof(T).Name}' produced duplicate ID '{id}'.");
            }

            entries[i] = item;
        }

        return new MaterializedContent(
            entries,
            index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            loaded);
    }

    private sealed class MaterializedContent(
        IReadOnlyList<T> entries,
        FrozenDictionary<string, (T Item, string? Digest)> index,
        IReadOnlyList<(string Id, T Item, string? Digest)> loaded)
    {
        public Lazy<string?> CollectionDigest { get; } = new(() =>
        {
            var builder = new StringBuilder();
            foreach (var (id, _, entryDigest) in loaded)
            {
                if (entryDigest is not { } digest) { return null; }
                BuildFingerprint.AppendPart(builder, id);
                BuildFingerprint.AppendPart(builder, digest);
            }
            return BuildFingerprint.HashText(builder.ToString());
        });
        public IReadOnlyList<T> Entries { get; } = entries;

        public FrozenDictionary<string, (T Item, string? Digest)> Index { get; } = index;

    }
}
