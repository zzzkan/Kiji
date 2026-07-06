namespace Kiji.Markdown;

public sealed class MarkdownContent<TFrontMatter>
{
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync;
    private readonly Lock _renderLock = new();
    private string? _cachedRendered;
    private bool _hasCachedRendered;
    private Task<string>? _inFlightRenderTask;

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
        if (_hasCachedRendered)
        {
            return _cachedRendered!;
        }

        Task<string> renderTask;
        lock (_renderLock)
        {
            if (_hasCachedRendered)
            {
                return _cachedRendered!;
            }

            _inFlightRenderTask ??= RenderCoreAsync();
            renderTask = _inFlightRenderTask;
        }

        return cancellationToken.CanBeCanceled
            ? await renderTask.WaitAsync(cancellationToken)
            : await renderTask;
    }

    private async Task<string> RenderCoreAsync()
    {
        try
        {
            var rendered = await _renderAsync(this, CancellationToken.None);

            lock (_renderLock)
            {
                _cachedRendered = rendered;
                _hasCachedRendered = true;
                _inFlightRenderTask = null;
            }

            return rendered;
        }
        catch
        {
            lock (_renderLock)
            {
                _inFlightRenderTask = null;
            }

            throw;
        }
    }
}