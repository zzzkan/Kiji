using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>
/// Collects the inputs a page render actually touched (content files, whole content
/// sets) and the extra files it materialized (e.g. image variants).
/// Attached to <see cref="Rendering.PageRenderContext"/> during incremental builds;
/// the recorded set becomes the page's dependency list in the build manifest.
/// </summary>
internal sealed class BuildDependencyRecorder
{
    private readonly ConcurrentDictionary<string, byte> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _additionalOutputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _contentSetScopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the render read the given source file.</summary>
    internal void AddFile(string absolutePath)
    {
        _files.TryAdd(absolutePath, 0);
    }

    /// <summary>Records a file the render wrote in addition to the page HTML.</summary>
    internal void AddOutput(string absolutePath)
    {
        _additionalOutputs.TryAdd(absolutePath, 0);
    }

    /// <summary>
    /// Records that the render observed the shape of a content set (e.g. enumerated a
    /// collection), making it dependent on every content file under
    /// <paramref name="scope"/> — a contents-relative directory, empty for the whole tree.
    /// </summary>
    internal void MarkContentSetDependency(string scope)
    {
        _contentSetScopes.TryAdd(scope, 0);
    }

    /// <summary>
    /// The recorded content-set scopes, in a stable order. Most pages record none — a
    /// keyed lookup depends on one file, not the set — so the empty case allocates
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// These return sorted arrays rather than the underlying keys because
    /// <see cref="ConcurrentDictionary{TKey, TValue}.Keys"/> copies into a new list on
    /// every access, and the manifest needs a deterministic order anyway. Sorting here
    /// keeps that from becoming a LINQ chain per page in the manifest pass.
    /// </remarks>
    internal string[] ContentSetScopes => Snapshot(_contentSetScopes, StringComparer.OrdinalIgnoreCase);

    internal string[] Files => Snapshot(_files, StringComparer.OrdinalIgnoreCase);

    internal string[] AdditionalOutputs => Snapshot(_additionalOutputs, StringComparer.OrdinalIgnoreCase);

    private static string[] Snapshot(ConcurrentDictionary<string, byte> source, StringComparer comparer)
    {
        if (source.IsEmpty)
        {
            return [];
        }

        // Enumerating the dictionary itself avoids the copy Keys makes; the recorder is
        // only read once its page has finished rendering, so nothing is being added.
        var values = new List<string>(source.Count);
        foreach (var pair in source)
        {
            values.Add(pair.Key);
        }

        var snapshot = values.ToArray();
        Array.Sort(snapshot, comparer);
        return snapshot;
    }
}
