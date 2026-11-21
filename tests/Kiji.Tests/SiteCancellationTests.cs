using Kiji.Assets;
using Kiji.Markdown;
using Kiji.Paths;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class SiteCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-cancellation-{Guid.NewGuid():N}");

    [Fact]
    public async Task RunAsync_CancelsMarkdownProcessor_PreservesSuccessfulCache_AndCanRecover()
    {
        WriteContent("original");
        var output = Path.Combine(_root, "dist");
        await using (var original = CreateApp(new TrackingImageProcessor()))
        {
            await original.PublishAsync(output);
        }
        var cache = Path.Combine(_root, ".kiji", "cache");
        var manifest = File.ReadAllBytes(Path.Combine(cache, "manifest.json"));
        var htmlPath = Path.Combine(output, "md", "post", "index.html");
        var originalHtml = File.ReadAllText(htmlPath);
        var abandoned = Path.Combine(cache, "work", "abandoned");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "unfinished"), "interrupted image work");
        WriteContent("changed");

        using var cancellation = new CancellationTokenSource();
        var started = Signal();
        var release = Signal();
        var processor = new TrackingImageProcessor
        {
            BeforeWriteAsync = async token =>
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            },
        };
        var running = CreateApp(processor, runtimeMetadata: new SiteRuntimeMetadata(OutputPath: output));
        var run = running.Host.RunAsync(cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(Directory.Exists(abandoned));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(cache, "manifest.json")));
            Assert.Equal(originalHtml, File.ReadAllText(htmlPath));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAny(run).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(cache, "work")));
        await using var recovered = CreateApp(new TrackingImageProcessor());
        await recovered.PublishAsync(output);
        Assert.Contains("changed", File.ReadAllText(htmlPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentRefresh_RetriesAnUnsentResponseAgainstTheNewSnapshot()
    {
        WriteContent("dev");
        var started = Signal();
        var calls = 0;
        var processor = new TrackingImageProcessor
        {
            BeforeWriteAsync = async token =>
            {
                if (Interlocked.Increment(ref calls) != 1) { return; }
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
        };
        await using var app = CreateApp(processor, serve: true);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = client.GetStringAsync("/md/post/", timeout.Token);
            await started.Task.WaitAsync(timeout.Token);
            await server.ReloadAfterCodeUpdateAsync().WaitAsync(timeout.Token);
            Assert.Contains("<img ", await request, StringComparison.Ordinal);
            Assert.Equal(2, calls);
            await web.StopAsync();
        }
    }

    [Theory]
    [InlineData("run")]
    [InlineData("host")]
    [InlineData("reload")]
    public async Task DevShutdown_CancelsAndJoinsDetachedMarkdown(string trigger)
    {
        WriteContent("dev");
        using var runStop = new CancellationTokenSource();
        using var requestStop = new CancellationTokenSource();
        var started = Signal();
        var canceled = Signal();
        var cleanup = Signal();
        var release = Signal();
        var processor = new TrackingImageProcessor
        {
            BeforeWriteAsync = async token =>
            {
                started.SetResult();
                try { await release.Task.WaitAsync(token); }
                catch (OperationCanceledException)
                {
                    canceled.SetResult();
                    await cleanup.Task;
                    throw;
                }
            },
        };
        await using var app = CreateApp(processor, serve: true);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, runStop.Token);
        using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
        var request = client.GetStringAsync("/md/post/", requestStop.Token);
        Task? shutdown = null;
        var hostShutdown = Task.CompletedTask;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            requestStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            if (trigger == "run") { runStop.Cancel(); }
            else if (trigger == "host") { hostShutdown = web.StopAsync(); }
            else { shutdown = server.ReloadAfterCodeUpdateAsync(); }
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await hostShutdown.WaitAsync(TimeSpan.FromSeconds(10));
            shutdown ??= server.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            cleanup.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            requestStop.Cancel();
            cleanup.TrySetResult();
            release.TrySetResult();
            await hostShutdown.WaitAsync(TimeSpan.FromSeconds(10));
            if (shutdown is not null) { await shutdown.WaitAsync(TimeSpan.FromSeconds(10)); }
            await web.StopAsync();
            await server.DisposeAsync();
        }
    }

    private TestSiteHost CreateApp(TrackingImageProcessor processor, SiteRuntimeMetadata? runtimeMetadata = null, bool serve = false)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root, Path.Combine(_root, "obj", "site")), runtimeMetadata ?? new SiteRuntimeMetadata(OutputPath: serve ? null : "dist"));
        builder.UseSiteInfo(new SiteInfo { Name = "Cancellation", BaseUrl = new Uri("https://example.test/") });
        TestSiteAssets.Bind(builder.GetSitePaths(), Path.Combine(_root, "wwwroot"));
        builder.Services.AddSingleton<IImageProcessor>(_ => processor);
        builder.AddMarkdownContent<FrontMatter>();
        builder.AddPageParameters<MarkdownPostTestPage>(provider => provider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>()
            .Select(post => new { Slug = "post", ContentKey = post.Path }));
        return builder.BuildSite();
    }

    private void WriteContent(string text)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(_root, "content", "post.md"), $"---\ntitle: Post\n---\n{text}\n![image](source.png)");
        File.WriteAllText(Path.Combine(_root, "content", "source.png"), text);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
