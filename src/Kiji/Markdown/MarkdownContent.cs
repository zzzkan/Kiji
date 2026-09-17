using System.Collections.Concurrent;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>A Markdown source with parsed front matter and a lazily rendered body.</summary>
public sealed class MarkdownContent<TFrontMatter>
{
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync;

    // Rendered HTML is cached per page route: the markup is identical everywhere, but
    // rendering also materializes referenced images into the rendering page's output
    // directory, so each page that embeds this content must run the pipeline once.
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _renderTasksByRoute = new(StringComparer.Ordinal);

    internal MarkdownContent(
        FileInfo fileInfo,
        TFrontMatter frontMatter,
        string body,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(renderAsync);

        FileInfo = fileInfo;
        FrontMatter = frontMatter;
        Body = body;
        _renderAsync = renderAsync;
    }

    /// <summary>The source file metadata.</summary>
    public FileInfo FileInfo { get; }

    /// <summary>
    /// The markdown body (front matter stripped) captured when the file was read for
    /// parsing, so rendering does not read the file again.
    /// </summary>
    internal string Body { get; }

    /// <summary>The parsed front matter.</summary>
    public TFrontMatter FrontMatter
    {
        get
        {
            // Reading front matter during a tracked render makes the page depend on
            // this file, so front-matter-only pages re-render when the file changes.
            PageRenderContext.Current?.Dependencies?.AddFile(FileInfo.FullName);
            return field;
        }
    }

    /// <summary>Renders the Markdown body as HTML and writes referenced image variants beside the current page.</summary>
    public async ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PageRenderContext.Current?.Dependencies?.AddFile(FileInfo.FullName);
        // A tracked render must observe all dependencies and materialize its outputs
        // again, even if the same content instance was rendered in an earlier build.
        if (PageRenderContext.Current?.Dependencies is not null)
        {
            return await _renderAsync(this, cancellationToken);
        }

        var cacheKey = PageRenderContext.Current?.RoutePath ?? string.Empty;
        var cached = _renderTasksByRoute.GetOrAdd(cacheKey,
            _ => new Lazy<Task<string>>(() => _renderAsync(this, CancellationToken.None)));
        try
        {
            var renderTask = cached.Value;
            return cancellationToken.CanBeCanceled
                ? await renderTask.WaitAsync(cancellationToken)
                : await renderTask;
        }
        catch
        {
            // Do not evict a shared render merely because one waiting request left.
            if (!cached.IsValueCreated || cached.Value.IsCompleted)
            {
                _renderTasksByRoute.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, cached));
            }
            throw;
        }
    }
}
