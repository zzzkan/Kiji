using Kiji.Markdown;
using Kiji.Tests.TestSite.PageServices;
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
        await using var app = CreateApp(processor);
        var run = app.RunAsync(name => name == "KIJI_OUTPUT" ? output : null, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(cache, "manifest.json")));
            Assert.Equal(originalHtml, File.ReadAllText(htmlPath));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await using var recovered = CreateApp(new TrackingImageProcessor());
        await recovered.PublishAsync(output);
        Assert.Contains("changed", File.ReadAllText(htmlPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("host")]
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
        await using var app = CreateApp(processor);
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
            // Reload drops the content dictionary, but its detached producer is still owned.
            await server.ReloadAfterCodeUpdateAsync();
            if (trigger == "run") { runStop.Cancel(); }
            else { hostShutdown = web.StopAsync(); }
            // Observe the selected stop signal before disposal can cancel the producer itself.
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await hostShutdown.WaitAsync(TimeSpan.FromSeconds(10));
            shutdown = server.DisposeAsync().AsTask();
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
            else
            {
                await web.StopAsync();
                await server.DisposeAsync();
            }
        }
    }

    private StaticSite CreateApp(TrackingImageProcessor processor)
    {
        var app = StaticSite.Create([]);
        app.Info = new SiteInfo { Name = "Cancellation", BaseUrl = new Uri("https://example.test/") };
        app.Paths.RootDirectory = _root;
        app.UseImageProcessor(() => processor).UseMarkdownContent<FrontMatter>();
        app.AddPages<MarkdownPostTestPage>(provider => provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(post => new { Slug = "post", ContentKey = post.Key }));
        return app;
    }

    private void WriteContent(string text)
    {
        Directory.CreateDirectory(Path.Combine(_root, "contents"));
        File.WriteAllText(Path.Combine(_root, "contents", "post.md"), $"---\ntitle: Post\n---\n{text}\n![image](source.png)");
        File.WriteAllText(Path.Combine(_root, "contents", "source.png"), text);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
