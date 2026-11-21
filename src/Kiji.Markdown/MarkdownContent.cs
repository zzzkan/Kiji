using Kiji.Incremental;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>A Markdown file: its front matter, and a body rendered on demand.</summary>
public sealed class MarkdownContent<TFrontMatter>
{
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync;

    private readonly Lock _renderLock = new();
    private Lazy<Task<string>>? _renderTask;
    private string? _frontMatterDependencyKey;
    private int _rendered;

    internal MarkdownContent(
        FileInfo fileInfo,
        TFrontMatter frontMatter,
        string body,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
        string? contentHash = null,
        string? path = null,
        string? frontMatterDigest = null)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(renderAsync);

        SourcePath = fileInfo.FullName;
        Path = path ?? fileInfo.Name;
        ContentHash = contentHash ?? BuildFingerprint.HashFile(fileInfo.FullName);
        FrontMatterDigest = frontMatterDigest ?? ContentHash;
        FrontMatter = frontMatter;
        Body = body;
        _renderAsync = renderAsync;
    }

    internal string ContentHash { get; }

    internal string FrontMatterDigest { get; }

    internal bool WasRendered => Volatile.Read(ref _rendered) != 0;

    internal void TrackFrontMatter(string sourceId) =>
        _frontMatterDependencyKey = sourceId + "#front-matter/" + Path.ToUpperInvariant();

    /// <summary>The path under the content directory, with <c>/</c> separators; also its collection ID.</summary>
    public string Path { get; }

    internal string SourcePath { get; }

    internal string Body { get; }

    /// <summary>The parsed front matter.</summary>
    public TFrontMatter FrontMatter
    {
        get
        {
            var dependencies = PageRenderContext.Current?.Dependencies;
            if (_frontMatterDependencyKey is { } key)
            {
                dependencies?.AddValue(key, FrontMatterDigest);
            }
            else
            {
                dependencies?.AddFile(SourcePath, ContentHash);
            }
            return field;
        }
    }

    /// <summary>Renders the body as HTML, writing its image variants.</summary>
    public async ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _rendered, 1);
        var context = PageRenderContext.Current;
        var pageToken = context?.CancellationToken ?? default;
        using var linked = cancellationToken.CanBeCanceled && pageToken.CanBeCanceled && cancellationToken != pageToken
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pageToken)
            : null;
        cancellationToken = linked?.Token ?? (cancellationToken.CanBeCanceled ? cancellationToken : pageToken);
        cancellationToken.ThrowIfCancellationRequested();
        context?.Dependencies?.AddFile(SourcePath, ContentHash);
        if (context?.Dependencies is not null)
        {
            var html = await _renderAsync(this, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return html;
        }

        var cached = GetSharedRender(context?.SharedRenders);
        while (cached.IsValueCreated && cached.Value.IsCompleted && !cached.Value.IsCompletedSuccessfully)
        {
            RemoveSharedRender(cached);
            cached = GetSharedRender(context?.SharedRenders);
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
            if (!cached.IsValueCreated || (cached.Value.IsCompleted && !cached.Value.IsCompletedSuccessfully))
            {
                RemoveSharedRender(cached);
            }
            throw;
        }
    }

    private Lazy<Task<string>> GetSharedRender(SharedRenderLifetime? lifetime)
    {
        lock (_renderLock)
        {
            return _renderTask ??= CreateSharedRender(lifetime);
        }
    }

    private void RemoveSharedRender(Lazy<Task<string>> cached)
    {
        lock (_renderLock)
        {
            if (ReferenceEquals(_renderTask, cached)) { _renderTask = null; }
        }
    }

    private Lazy<Task<string>> CreateSharedRender(SharedRenderLifetime? lifetime)
    {
        Lazy<Task<string>> cached = null!;
        cached = new Lazy<Task<string>>(() =>
        {
            var task = lifetime is null
                ? _renderAsync(this, CancellationToken.None)
                : lifetime.RunAsync(token => _renderAsync(this, token));
            _ = EvictFailedRenderAsync(task, cached);
            return task;
        });
        return cached;
    }

    private async Task EvictFailedRenderAsync(Task<string> render, Lazy<Task<string>> cached)
    {
        try { await render; }
        catch { RemoveSharedRender(cached); }
    }
}
