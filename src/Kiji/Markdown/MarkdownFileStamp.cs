namespace Kiji.Markdown;

/// <summary>
/// A file's size and last write time, as reported by the directory walk that found it.
/// </summary>
/// <remarks>
/// Carrying it explicitly is what keeps the build from asking the filesystem the same
/// question repeatedly: a directory enumeration already returns this metadata, while
/// constructing a <see cref="FileInfo"/> from a path goes back to disk for it — 54 ms
/// against 28 ms over a thousand files (<c>Kiji.Benchmarks DirectoryScanBenchmarks</c>).
/// </remarks>
internal readonly record struct MarkdownFileStamp(long Length, DateTime LastWriteTimeUtc)
{
    internal static MarkdownFileStamp From(FileInfo file)
    {
        return new MarkdownFileStamp(file.Length, file.LastWriteTimeUtc);
    }
}
