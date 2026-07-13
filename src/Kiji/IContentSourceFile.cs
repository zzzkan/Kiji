namespace Kiji;

/// <summary>
/// Implemented by content items backed by a single source file, letting the
/// incremental build attribute item-level dependencies (and propagate them through
/// <see cref="ContentCollection{T}.Map{TResult}"/> projections) to that file.
/// </summary>
internal interface IContentSourceFile
{
    /// <summary>The absolute path of the item's source file.</summary>
    string SourceFilePath { get; }
}
