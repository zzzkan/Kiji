namespace Kiji;

/// <summary>
/// Implemented by content items backed by a single source file, letting the
/// incremental build attribute item-level dependencies to that file. Optional: items
/// without a source file simply omit it, and their lookups fall back to a dependency
/// on the whole content set.
/// </summary>
internal interface IContentSourceFile
{
    /// <summary>The absolute path of the item's source file.</summary>
    string SourceFilePath { get; }
}
