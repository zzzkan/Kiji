using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Kiji.Hosting;
using Kiji.Rendering;

namespace Kiji;

/// <summary>A lazily loaded, case-insensitive dictionary of site content.</summary>
/// <remarks>Resolve it through dependency injection; entries enumerate in ascending key order.</remarks>
public sealed class ContentDictionary<T> : IReadOnlyDictionary<string, T>
    where T : class
{
    private readonly ContentRuntime _runtime;
    private readonly Func<IServiceProvider, ContentSourceItems<T>> _load;
    private readonly Func<T, string> _key;
    private readonly ContentSourceOptions<T> _options;

    internal ContentDictionary(
        ContentRuntime runtime,
        Func<IServiceProvider, ContentSourceItems<T>> load,
        Func<T, string> key,
        ContentSourceOptions<T> options,
        string contentSetScope = "")
    {
        _runtime = runtime;
        _load = load;
        _key = key;
        _options = options;
        ContentSetScope = contentSetScope;
    }

    /// <summary>
    /// The number of items in the dictionary.
    /// </summary>
    public int Count => Observed.Entries.Count;

    /// <summary>
    /// Every key, in ascending order.
    /// </summary>
    public IEnumerable<string> Keys => Observed.Entries.Select(static entry => entry.Key);

    /// <summary>
    /// Every item, in ascending key order.
    /// </summary>
    public IEnumerable<T> Values => Observed.Entries.Select(static entry => entry.Value);

    /// <summary>
    /// The item with the given key, compared case-insensitively.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No item carries that key.</exception>
    public T this[string key] => TryGetValue(key, out var item)
        ? item
        : throw new KeyNotFoundException($"Content item with key '{key}' was not found.");

    /// <summary>
    /// The content directory this dictionary reads, relative to the contents root;
    /// empty for the whole tree. Pages that enumerate it depend on this
    /// subtree rather than on every content file in the site.
    /// </summary>
    internal string ContentSetScope { get; }

    /// <summary>
    /// Finds an item by its key, compared case-insensitively.
    /// </summary>
    public bool TryGetValue(string key, [NotNullWhen(true)] out T? item)
    {
        ArgumentNullException.ThrowIfNull(key);

        var materialized = Materialized;
        if (!materialized.Index.TryGetValue(key, out item))
        {
            // A miss still observed the key set, so it is only valid while the key set
            // holds — that is the content set, not any one file.
            MarkContentSetDependency();
            return false;
        }

        RecordItemDependency(materialized, key);
        return true;
    }

    /// <summary>
    /// Whether an item with the given key exists.
    /// </summary>
    public bool ContainsKey(string key)
    {
        return TryGetValue(key, out _);
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
    {
        return Observed.Entries.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private MaterializedContent Materialized => _runtime.GetOrMaterialize(this, Materialize);

    /// <summary>
    /// The dictionary as seen by a member that exposes its shape (count, keys, values,
    /// enumeration). Observing the shape couples the page to the content set: a list
    /// page has to reflect items being added and removed, not just edited. Keyed
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
        PageRenderContext.Current?.Dependencies?.MarkContentSetDependency(ContentSetScope);
    }

    private void RecordItemDependency(MaterializedContent materialized, string key)
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
            dependencies.MarkContentSetDependency(ContentSetScope);
        }
    }

    private MaterializedContent Materialize(IServiceProvider services)
    {
        var loaded = _load(services);
        var items = loaded.Items;
        var provenance = loaded.Provenance ?? DeriveProvenance(items);

        var keys = new string[items.Count];
        var failures = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            keys[i] = _key(items[i]);
            if (string.IsNullOrWhiteSpace(keys[i]))
            {
                failures.Add($"{Describe(provenance[i])}: produced an empty key.");
            }
        }

        var index = new Dictionary<string, T>(items.Count, StringComparer.OrdinalIgnoreCase);
        var provenanceByKey = new Dictionary<string, string?>(items.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(keys[i]))
            {
                continue;
            }

            if (!index.TryAdd(keys[i], items[i]))
            {
                throw new InvalidOperationException(
                    $"ContentDictionary<{typeof(T).Name}> contains duplicate key '{keys[i]}' ({Describe(provenance[i])} and {Describe(provenanceByKey[keys[i]])}). Keys must be unique (case-insensitive).");
            }

            provenanceByKey[keys[i]] = provenance[i];
        }

        Validate(items, keys, provenance, failures);

        // Ascending key order, so the generated route set — and therefore the sitemap
        // and every page's output path — is identical from one build to the next.
        var entries = index
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        return new MaterializedContent(
            entries,
            index.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            provenanceByKey.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Runs every validator over every item and reports the failures together, along
    /// with any empty keys. Fixing content one rebuild at a time is the thing worth
    /// avoiding here, so the first failure does not stop the pass.
    /// </summary>
    private void Validate(
        IReadOnlyList<T> items,
        string[] keys,
        IReadOnlyList<string?> provenance,
        List<string> failures)
    {
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var validator in _options.Validators)
            {
                try
                {
                    validator(items[i]);
                }
                catch (Exception exception)
                {
                    failures.Add($"{Describe(provenance[i], keys[i])}: {exception.Message}");
                }
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        const int MaxReported = 10;
        var reported = string.Join(Environment.NewLine, failures.Take(MaxReported).Select(static failure => "  " + failure));
        var remainder = failures.Count > MaxReported
            ? $"{Environment.NewLine}  ... and {failures.Count - MaxReported} more."
            : string.Empty;

        throw new InvalidOperationException(
            $"ContentDictionary<{typeof(T).Name}> has {failures.Count} invalid item(s):{Environment.NewLine}{reported}{remainder}");
    }

    private static string Describe(string? sourceFilePath, string? key = null)
    {
        return sourceFilePath
            ?? (string.IsNullOrWhiteSpace(key) ? "<no source file>" : $"'{key}'");
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

    private sealed class MaterializedContent(
        IReadOnlyList<KeyValuePair<string, T>> entries,
        FrozenDictionary<string, T> index,
        FrozenDictionary<string, string?> provenanceByKey)
    {
        public IReadOnlyList<KeyValuePair<string, T>> Entries { get; } = entries;

        public FrozenDictionary<string, T> Index { get; } = index;

        public FrozenDictionary<string, string?> ProvenanceByKey { get; } = provenanceByKey;
    }
}
