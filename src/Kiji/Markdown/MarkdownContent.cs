using System.Collections.Concurrent;
using Kiji.Rendering;

namespace Kiji.Markdown;

public sealed class MarkdownContent<TFrontMatter>
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

    public MarkdownFileInfo FileInfo { get; }

    public TFrontMatter FrontMatter { get; }

    public async ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
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
