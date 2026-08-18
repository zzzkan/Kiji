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

    internal IReadOnlyCollection<string> ContentSetScopes => (IReadOnlyCollection<string>)_contentSetScopes.Keys;

    internal IReadOnlyCollection<string> Files => (IReadOnlyCollection<string>)_files.Keys;

    internal IReadOnlyCollection<string> AdditionalOutputs => (IReadOnlyCollection<string>)_additionalOutputs.Keys;
}
