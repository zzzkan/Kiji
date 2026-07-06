namespace Kiji.Markdown;

public sealed record MarkdownFileInfo(
    string ContentsDirectory,
    string FilePath,
    string RelativePath,
    string RelativeDirectoryPath,
    string FileNameWithoutExtension,
    DateTime SourceLastWriteTimeUtc)
{
    public static MarkdownFileInfo Create(string contentsDirectory, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedContentsDirectory = Path.GetFullPath(contentsDirectory);
        var normalizedFilePath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(normalizedContentsDirectory, normalizedFilePath);
        var relativeDirectoryPath = Path.GetDirectoryName(relativePath) ?? string.Empty;

        return new MarkdownFileInfo(
            normalizedContentsDirectory,
            normalizedFilePath,
            relativePath,
            relativeDirectoryPath,
            Path.GetFileNameWithoutExtension(normalizedFilePath),
            File.GetLastWriteTimeUtc(normalizedFilePath));
    }
}