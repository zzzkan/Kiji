using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Kiji.Incremental;
using Kiji.Rendering;

namespace Kiji;

/// <summary>Site content, loaded on first use and tracked for incremental builds. Resolve it through DI.</summary>
/// <remarks>A Markdown ID is its path under the content directory, such as <c>posts/hello.md</c>; it is not a URL.</remarks>
public sealed class ContentCollection<T> : IReadOnlyCollection<T>
    where T : class
{
    private readonly string _sourceId;
    private readonly ContentRuntime _runtime;
    private readonly Func<IServiceProvider, string, IReadOnlyList<(string Id, T Item, string? Digest, string? ProjectionDigest, string? FrontMatterDigest)>> _load;

    internal ContentCollection(
        ContentRuntime runtime,
        Func<IServiceProvider, string, IReadOnlyList<(string Id, T Item, string? Digest, string? ProjectionDigest, string? FrontMatterDigest)>> load,
        string contentSetScope = "")
    {
        _runtime = runtime;
        _load = load;
        _sourceId = typeof(T).FullName + ":" + contentSetScope;
        runtime.Dependencies.Register(_sourceId, CollectionDigest);
        runtime.Dependencies.RegisterSource(_sourceId, key => Materialized.Index.GetValueOrDefault(key).Digest);
        runtime.Dependencies.RegisterSource(_sourceId + "#front-matter", id => Materialized.Index.GetValueOrDefault(id).FrontMatterDigest);
    }

    /// <inheritdoc/>
    public int Count => Observed.Entries.Count;

    /// <summary>The item with this ID, ignoring case.</summary>
    /// <exception cref="KeyNotFoundException">No item has the ID.</exception>
    public T this[string id] => TryGetValue(id, out var item)
        ? item
        : throw new KeyNotFoundException($"Content item with ID '{id}' was not found.");

    /// <summary>Finds the item with this ID, ignoring case.</summary>
    public bool TryGetValue(string id, [NotNullWhen(true)] out T? item)
    {
        ArgumentNullException.ThrowIfNull(id);

        var materialized = Materialized;
        if (!materialized.Index.TryGetValue(id, out var entry))
        {
            MarkContentSetDependency();
            item = null;
            return false;
        }

        item = entry.Item;
        var dependencies = PageRenderContext.Current?.Dependencies;
        if (dependencies is not null)
        {
            dependencies.AddValue(_sourceId + "/" + id.ToUpperInvariant(), entry.Digest);
        }
        return true;
    }

    /// <summary>Whether an item has this ID, ignoring case.</summary>
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
        var page = PageRenderContext.Current;
        PageRenderContext.SetCurrent(null);
        IReadOnlyList<(string Id, T Item, string? Digest, string? ProjectionDigest, string? FrontMatterDigest)> loaded;
        try { loaded = _load(services, _sourceId); }
        finally { PageRenderContext.SetCurrent(page); }

        var index = new Dictionary<string, (T Item, string? Digest, string? FrontMatterDigest)>(loaded.Count, StringComparer.OrdinalIgnoreCase);
        var entries = new T[loaded.Count];
        var projections = new (string Id, string? Digest)[loaded.Count];
        for (var i = 0; i < loaded.Count; i++)
        {
            var (id, item, digest, projectionDigest, frontMatterDigest) = loaded[i];
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    $"The internal content loader for '{typeof(T).Name}' produced an empty ID.");
            }

            if (!index.TryAdd(id, (item, digest, frontMatterDigest)))
            {
                throw new InvalidOperationException(
                    $"The internal content loader for '{typeof(T).Name}' produced duplicate ID '{id}'.");
            }

            _runtime.RegisterItem(item, _sourceId, id, digest);
            entries[i] = item;
            projections[i] = (id, projectionDigest);
        }

        return new MaterializedContent(
            entries,
            index,
            projections);
    }

    private sealed class MaterializedContent(
        IReadOnlyList<T> entries,
        Dictionary<string, (T Item, string? Digest, string? FrontMatterDigest)> index,
        IReadOnlyList<(string Id, string? Digest)> projections)
    {
        public Lazy<string?> CollectionDigest { get; } = new(() =>
        {
            var builder = new StringBuilder();
            foreach (var (id, entryDigest) in projections)
            {
                if (entryDigest is not { } digest) { return null; }
                BuildFingerprint.AppendPart(builder, id);
                BuildFingerprint.AppendPart(builder, digest);
            }
            return BuildFingerprint.HashText(builder.ToString());
        });
        public IReadOnlyList<T> Entries { get; } = entries;

        public Dictionary<string, (T Item, string? Digest, string? FrontMatterDigest)> Index { get; } = index;
    }
}
