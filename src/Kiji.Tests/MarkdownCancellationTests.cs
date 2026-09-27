using Kiji.Generation;
using Kiji.Assets;
using Kiji.Markdown;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackedRender_ObservesBothPageAndExplicitCancellation(bool cancelExplicit)
    {
        using var page = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var started = Signal();
        var content = Content(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return "unreachable";
        });
        PageRenderContext.SetCurrent(Context(page.Token, tracked: true));
        try
        {
            var render = content.RenderAsync(caller.Token).AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (cancelExplicit ? caller : page).Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => render.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { PageRenderContext.SetCurrent(null); }
    }

    [Fact]
    public async Task SharedRender_RequestCancellationLeavesOtherWaiterAndProducerAlive()
    {
        await using var lifetime = new SharedRenderLifetime(default, default);
        using var request = new CancellationTokenSource();
        var started = Signal();
        var release = Signal();
        var calls = 0;
        var content = Content(async token =>
        {
            calls++;
            started.SetResult();
            await release.Task.WaitAsync(token);
            return "shared HTML";
        });
        try
        {
            PageRenderContext.SetCurrent(Context(request.Token, lifetime));
            var first = content.RenderAsync().AsTask();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            PageRenderContext.SetCurrent(Context(default, lifetime));
            var second = content.RenderAsync().AsTask();
            request.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            release.SetResult();
            Assert.Equal("shared HTML", await second.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            PageRenderContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task CanceledSharedRender_CanRetry()
    {
        var calls = 0;
        var content = Content(_ => ++calls == 1
            ? Task.FromCanceled<string>(new CancellationToken(true))
            : Task.FromResult("retry"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => content.RenderAsync().AsTask());
        Assert.Equal("retry", await content.RenderAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DetachedFailedProducer_DoesNotPoisonNextRequest()
    {
        using var caller = new CancellationTokenSource();
        var firstProducer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var content = Content(_ => ++calls == 1 ? firstProducer.Task : Task.FromResult("retry"));
        var firstWaiter = content.RenderAsync(caller.Token).AsTask();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstWaiter);
        firstProducer.SetException(new IOException("failed without waiters"));
        Assert.Equal("retry", await content.RenderAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellationInPostProcessor_StopsRemainingMarkdownStages()
    {
        using var cancellation = new CancellationTokenSource();
        var options = new MarkdownOptions();
        var secondCalled = false;
        options.AddHtmlPostProcessor(html => { cancellation.Cancel(); return html; });
        options.AddHtmlPostProcessor(html => { secondCalled = true; return html; });
        var root = Path.GetTempPath();
        var processor = new MarkdownProcessor(new ResolvedSitePaths
        {
            ContentDirectory = root,
            StaticDirectory = root,
            OutputDirectory = root,
        }, new ImageProcessor(), options);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessBodyAsync(
            Path.Combine(root, "post.md"), "# heading", cancellation.Token));
        Assert.False(secondCalled);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static MarkdownContent<FrontMatter> Content(Func<CancellationToken, Task<string>> render)
        => new(new FileInfo(Path.Combine(Path.GetTempPath(), "cancellation.md")), new FrontMatter(), "body",
            (_, token) => render(token), contentHash: "captured-source-hash");

    private static PageRenderContext Context(CancellationToken cancellationToken,
        SharedRenderLifetime? lifetime = null, bool tracked = false) => new()
        {
            RoutePath = "/post/",
            OutputRelativeDirectory = "post",
            OutputUrlDirectory = "/post/",
            CancellationToken = cancellationToken,
            SharedRenders = lifetime,
            Dependencies = tracked ? new BuildDependencyRecorder() : null,
        };
}
