namespace Kiji.Markdown;

/// <param name="Slug">
/// The page-bundle name of the file: the containing directory's name for
/// <c>index.md</c>, otherwise the file name without its extension. Not normalized —
/// it is the on-disk name verbatim. The usual choice for a markdown source's key.
/// </param>
public sealed record MarkdownFileInfo(
    string ContentsDirectory,
    string FilePath,
    string RelativePath,
    string RelativeDirectoryPath,
    string FileNameWithoutExtension,
    string Slug,
    DateTime SourceLastWriteTimeUtc)
{
    /// <summary>
    /// Describes a file whose stamp the caller already has — from the directory walk
    /// that found it — so nothing is asked of the filesystem here.
    /// </summary>
    internal static MarkdownFileInfo Create(string contentsDirectory, string filePath, MarkdownFileStamp stamp)
    {
        return Create(contentsDirectory, filePath, stamp.LastWriteTimeUtc);
    }

    public static MarkdownFileInfo Create(string contentsDirectory, string filePath)
    {
        return Create(contentsDirectory, filePath, lastWriteTimeUtc: null);
    }

    private static MarkdownFileInfo Create(string contentsDirectory, string filePath, DateTime? lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedContentsDirectory = Path.GetFullPath(contentsDirectory);
        var normalizedFilePath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(normalizedContentsDirectory, normalizedFilePath);
        var relativeDirectoryPath = Path.GetDirectoryName(relativePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedFilePath);

        return new MarkdownFileInfo(
            normalizedContentsDirectory,
            normalizedFilePath,
            relativePath,
            relativeDirectoryPath,
            fileNameWithoutExtension,
            CreateSlug(relativeDirectoryPath, fileNameWithoutExtension),
            lastWriteTimeUtc ?? File.GetLastWriteTimeUtc(normalizedFilePath));
    }

    /// <summary>
    /// The page-bundle convention: <c>posts/hello/index.md</c> is the page "hello", and
    /// so is <c>posts/hello.md</c>. Only the last directory segment is used, so nesting
    /// the bundle deeper does not change the name.
    /// </summary>
    private static string CreateSlug(string relativeDirectoryPath, string fileNameWithoutExtension)
    {
        return string.Equals(fileNameWithoutExtension, "index", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(relativeDirectoryPath)
            : fileNameWithoutExtension;
    }
}
