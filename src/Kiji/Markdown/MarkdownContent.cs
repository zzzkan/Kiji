using System.Collections.Concurrent;
using Kiji.Rendering;

namespace Kiji.Markdown;

public sealed class MarkdownContent<TFrontMatter> : IContentSourceFile
{
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync;

    // Rendered HTML is cached per page route: the markup is identical everywhere, but
    // rendering also materializes referenced images into the rendering page's output
    // directory, so each page that embeds this content must run the pipeline once.
    private readonly ConcurrentDictionary<string, Task<string>> _renderTasksByRoute = new(StringComparer.Ordinal);

    public MarkdownContent(
        MarkdownFileInfo fileInfo,
        TFrontMatter frontMatter,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentNullException.ThrowIfNull(renderAsync);

        FileInfo = fileInfo;
        FrontMatter = frontMatter;
        _renderAsync = renderAsync;
    }

    internal MarkdownContent(
        MarkdownFileInfo fileInfo,
        TFrontMatter frontMatter,
        string body,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync)
        : this(fileInfo, frontMatter, renderAsync)
    {
        Body = body;
    }

    public MarkdownFileInfo FileInfo { get; }

    /// <summary>
    /// The markdown body (front matter stripped) captured when the file was read for
    /// parsing, so rendering does not read the file again. Null when constructed via
    /// the public constructor; renderers must then read the file themselves.
    /// </summary>
    internal string? Body { get; }

    public TFrontMatter FrontMatter
    {
        get
        {
            // Reading front matter during a tracked render makes the page depend on
            // this file, so front-matter-only pages re-render when the file changes.
            PageRenderContext.Current?.Dependencies?.AddFile(FileInfo.FilePath);
            return field;
        }
    }

    string IContentSourceFile.SourceFilePath => FileInfo.FilePath;

    public async ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        PageRenderContext.Current?.Dependencies?.AddFile(FileInfo.FilePath);
        var cacheKey = PageRenderContext.Current?.RoutePath ?? string.Empty;
        var renderTask = _renderTasksByRoute.GetOrAdd(cacheKey, key => RenderCoreAsync(key));

        return cancellationToken.CanBeCanceled
            ? await renderTask.WaitAsync(cancellationToken)
            : await renderTask;
    }

    private async Task<string> RenderCoreAsync(string cacheKey)
    {
        try
        {
            return await _renderAsync(this, CancellationToken.None);
        }
        catch
        {
            _renderTasksByRoute.TryRemove(cacheKey, out _);
            throw;
        }
    }
}
