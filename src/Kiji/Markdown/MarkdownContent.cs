using System.Collections.Concurrent;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>A Markdown source with parsed front matter and a lazily rendered body.</summary>
public sealed class MarkdownContent<TFrontMatter>
{
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync;

    // Rendered HTML is cached per page route: local-image URLs and materialized variants
    // belong to the rendering page's output directory, so each page that embeds this
    // content must run the pipeline once.
    private ConcurrentDictionary<string, Lazy<Task<string>>>? _renderTasksByRoute;

    internal MarkdownContent(
        FileInfo fileInfo,
        TFrontMatter frontMatter,
        string body,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
        string? contentHash = null)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(renderAsync);

        FileInfo = fileInfo;
        ContentHash = contentHash ?? Generation.BuildFingerprint.HashFile(fileInfo.FullName);
        FrontMatter = frontMatter;
        Body = body;
        _renderAsync = renderAsync;
    }

    internal string ContentHash { get; }

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
            PageRenderContext.Current?.Dependencies?.AddFile(FileInfo.FullName, ContentHash);
            return field;
        }
    }

    /// <summary>Renders the Markdown body as HTML and writes referenced image variants beside the current page.</summary>
    public async ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        var context = PageRenderContext.Current;
        var pageToken = context?.CancellationToken ?? default;
        using var linked = cancellationToken.CanBeCanceled && pageToken.CanBeCanceled && cancellationToken != pageToken
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pageToken)
            : null;
        cancellationToken = linked?.Token ?? (cancellationToken.CanBeCanceled ? cancellationToken : pageToken);
        cancellationToken.ThrowIfCancellationRequested();
        context?.Dependencies?.AddFile(FileInfo.FullName, ContentHash);
        // A tracked render must observe all dependencies and materialize its outputs
        // again, even if the same content instance was rendered in an earlier build.
        if (context?.Dependencies is not null)
        {
            var html = await _renderAsync(this, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return html;
        }

        var cacheKey = context?.RoutePath ?? string.Empty;
        var tasks = LazyInitializer.EnsureInitialized(ref _renderTasksByRoute,
            static () => new(StringComparer.Ordinal));
        var cached = tasks.GetOrAdd(cacheKey, _ => CreateSharedRender(tasks, cacheKey, context?.SharedRenders));
        // A producer may have finished before its eviction continuation runs.
        while (cached.IsValueCreated && cached.Value.IsCompleted && !cached.Value.IsCompletedSuccessfully)
        {
            tasks.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, cached));
            cached = tasks.GetOrAdd(cacheKey, _ => CreateSharedRender(tasks, cacheKey, context?.SharedRenders));
        }
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
            if (!cached.IsValueCreated || (cached.Value.IsCompleted && !cached.Value.IsCompletedSuccessfully))
            {
                tasks.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, cached));
            }
            throw;
        }
    }

    private Lazy<Task<string>> CreateSharedRender(ConcurrentDictionary<string, Lazy<Task<string>>> tasks,
        string cacheKey, SharedRenderLifetime? lifetime)
    {
        Lazy<Task<string>> cached = null!;
        cached = new Lazy<Task<string>>(() =>
        {
            var task = lifetime is null
                ? _renderAsync(this, CancellationToken.None)
                : lifetime.RunAsync(token => _renderAsync(this, token));
            // Observe and evict failed shared renders even after every waiter has left.
            _ = EvictFailedRenderAsync(task, tasks, cacheKey, cached);
            return task;
        });
        return cached;
    }

    private static async Task EvictFailedRenderAsync(Task<string> render,
        ConcurrentDictionary<string, Lazy<Task<string>>> tasks, string cacheKey, Lazy<Task<string>> cached)
    {
        try { await render; }
        catch { tasks.TryRemove(new KeyValuePair<string, Lazy<Task<string>>>(cacheKey, cached)); }
    }
}
