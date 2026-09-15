namespace Kiji.Markdown;

/// <summary>
/// A file's size and last write time, as reported by the directory walk that found it.
/// </summary>
/// <remarks>Reuse the metadata obtained during directory enumeration.</remarks>
internal readonly record struct MarkdownFileStamp(long Length, DateTime LastWriteTimeUtc)
{
    internal static MarkdownFileStamp From(FileInfo file)
    {
        return new MarkdownFileStamp(file.Length, file.LastWriteTimeUtc);
    }
}
