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
        await using (var original = CreateApp(new CancellationImageProcessor(_ => Task.CompletedTask)))
        {
            await original.PublishAsync(output);
        }
        var cache = Path.Combine(_root, ".kiji", "cache");
        var manifest = File.ReadAllBytes(Path.Combine(cache, "manifest.json"));
        var bundles = Directory.GetFiles(cache, "html-*.bin").ToDictionary(path => path, File.ReadAllBytes);
        Assert.NotEmpty(bundles);
        var htmlPath = Path.Combine(output, "md", "post", "index.html");
        var originalHtml = File.ReadAllText(htmlPath);
        WriteContent("changed");

        using var cancellation = new CancellationTokenSource();
        var started = Signal();
        var release = Signal();
        var observedCancellation = false;
        var processor = new CancellationImageProcessor(async token =>
        {
            started.SetResult();
            try { await release.Task.WaitAsync(token); }
            catch (OperationCanceledException) { observedCancellation = true; throw; }
        });
        await using var app = CreateApp(processor);
        var run = app.RunAsync(name => name == "KIJI_OUTPUT" ? output : null, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.Equal(1, await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(observedCancellation);
            Assert.True(processor.Disposed);
            Assert.False(processor.DisposedWhileActive);
            Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(cache, "manifest.json")));
            foreach (var (path, bytes) in bundles) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
            Assert.Equal(originalHtml, File.ReadAllText(htmlPath));
            Assert.Empty(Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var recoveredProcessor = new CancellationImageProcessor(_ => Task.CompletedTask);
        await using var recovered = CreateApp(recoveredProcessor);
        await recovered.PublishAsync(output);
        Assert.Equal(1, recoveredProcessor.Calls);
        Assert.Contains("changed", File.ReadAllText(htmlPath), StringComparison.Ordinal);
        await recovered.PublishAsync(output);
        Assert.Equal(1, recoveredProcessor.Calls);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("host")]
    [InlineData("dispose")]
    public async Task DevShutdown_CancelsAndJoinsDetachedMarkdownBeforeProcessorDisposal(string trigger)
    {
        WriteContent("dev");
        using var runStop = new CancellationTokenSource();
        using var requestStop = new CancellationTokenSource();
        var started = Signal();
        var canceled = Signal();
        var cleanup = Signal();
        var release = Signal();
        var processor = new CancellationImageProcessor(async token =>
        {
            started.SetResult();
            try { await release.Task.WaitAsync(token); }
            catch (OperationCanceledException)
            {
                canceled.SetResult();
                await cleanup.Task;
                throw;
            }
        });
        await using var app = CreateApp(processor);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, runStop.Token);
        using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
        var request = client.GetStringAsync("/md/post/", requestStop.Token);
        Task? shutdown = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            requestStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            // Reload drops the content dictionary, but its detached producer is still owned.
            await server.ReloadAfterCodeUpdateAsync();
            if (trigger == "run") { runStop.Cancel(); }
            shutdown = ShutdownAsync();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(shutdown.IsCompleted);
            Assert.True(processor.Active);
            Assert.False(processor.Disposed);
            cleanup.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
            await app.DisposeAsync();
            Assert.True(processor.Disposed);
            Assert.False(processor.DisposedWhileActive);
        }
        finally
        {
            cleanup.TrySetResult();
            release.TrySetResult();
            if (shutdown is not null) { await shutdown.WaitAsync(TimeSpan.FromSeconds(10)); }
            else
            {
                await web.StopAsync();
                await server.DisposeAsync();
            }
        }

        async Task ShutdownAsync()
        {
            if (trigger == "host") { await web.StopAsync(); }
            await server.DisposeAsync();
        }
    }

    private StaticSite CreateApp(CancellationImageProcessor processor)
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
