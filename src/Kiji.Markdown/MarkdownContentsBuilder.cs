namespace Kiji.Markdown;

public sealed class MarkdownContentsBuilder<TFrontMatter>(
    string contentsDirectory,
    Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync)
{
    private readonly string _contentsDirectory = contentsDirectory;
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync = renderAsync;

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        if (!Directory.Exists(_contentsDirectory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {_contentsDirectory}");
        }

        var markdownFiles = Directory.EnumerateFiles(_contentsDirectory, "*.md", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<MarkdownContent<TFrontMatter>>(markdownFiles.Length);

        foreach (var markdownFile in markdownFiles)
        {
            var fileInfo = MarkdownFileInfo.Create(_contentsDirectory, markdownFile);
            var frontMatter = MarkdownFrontMatterParser.Parse<TFrontMatter>(markdownFile);
            items.Add(new MarkdownContent<TFrontMatter>(fileInfo, frontMatter, _renderAsync));
        }

        return items;
    }
}
