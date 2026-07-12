using System.Net;
using System.Net.WebSockets;
using System.Text;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Integration tests for the on-demand development server (Kestrel on an ephemeral port).
/// </summary>
public sealed class DevServerTests : IAsyncDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _staticDir;
    private KijiApp? _app;

    public DevServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DevServerTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "contents");
        _staticDir = Path.Combine(_testDir, "wwwroot");
        Directory.CreateDirectory(_contentsDir);
        Directory.CreateDirectory(_staticDir);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task Serve_RendersPageOnDemandWithLiveReloadScript()
    {
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs);
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/"));
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<title>Home - zzzkan.me</title>", html, StringComparison.Ordinal);
            Assert.Contains("/_kiji/livereload.js", html, StringComparison.Ordinal);

            var blogResponse = await client.GetAsync(new Uri(baseAddress, "/blog/hello-world/"));
            var blogHtml = await blogResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, blogResponse.StatusCode);
            Assert.Contains("<h1>Hello World</h1>", blogHtml, StringComparison.Ordinal);

            var output = logs.ToString();
            Assert.Contains("kiji dev     🚀 Started Kiji dev server at", output, StringComparison.Ordinal);
            Assert.Contains("kiji dev     ⌚ Watching content files under", output, StringComparison.Ordinal);
            Assert.Contains("kiji dev     ⌚ Watching static files under", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_ResolvesMissingTrailingSlashWithoutRedirect()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/blog"));
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<title>Blog - zzzkan.me</title>", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_RouteMatchingIsCaseSensitive()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/Blog/"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Serve_UnknownRouteRendersNotFoundPageWith404()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/no-such-page/"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_ContentChangeBroadcastsReloadAndServesUpdatedContent()
    {
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs);
        await using (devServer)
        {
            using var client = CreateClient();

            var initialHtml = await client.GetStringAsync(new Uri(baseAddress, "/blog/hello-world/"));
            Assert.Contains("<h1>Hello World</h1>", initialHtml, StringComparison.Ordinal);

            using var socket = new ClientWebSocket();
            var wsUri = new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri;
            await socket.ConnectAsync(wsUri, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.txt"), "Hello Updated");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));

            var updatedHtml = await client.GetStringAsync(new Uri(baseAddress, "/blog/hello-world/"));
            Assert.Contains("<h1>Hello Updated</h1>", updatedHtml, StringComparison.Ordinal);

            var output = logs.ToString();
            Assert.Contains($"File updated: .{Path.DirectorySeparatorChar}hello-world.txt", output, StringComparison.Ordinal);
            Assert.Contains("Reloaded 1 browser client(s).", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_StaticChangeBroadcastsReloadWithoutInvalidatingContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_staticDir, "site.css"), "body { color: black; }");

        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs);
        await using (devServer)
        {
            using var socket = new ClientWebSocket();
            var wsUri = new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri;
            await socket.ConnectAsync(wsUri, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(_staticDir, "site.css"), "body { color: red; }");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));

            var output = logs.ToString();
            Assert.Contains($"File updated: .{Path.DirectorySeparatorChar}site.css", output, StringComparison.Ordinal);
            Assert.Contains("Reloaded 1 browser client(s).", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_CodeUpdateNotificationBroadcastsReload()
    {
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs);
        await using (devServer)
        {
            using var socket = new ClientWebSocket();
            var wsUri = new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri;
            await socket.ConnectAsync(wsUri, CancellationToken.None);

            Kiji.Hosting.DevServer.NotifyCodeUpdated();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    [Fact]
    public void DeduplicateChanges_CollapsesRepeatedEventsPerFileKeepingLatestChangeType()
    {
        List<Kiji.Hosting.WatchedChange> events =
        [
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Created, @"2026\post\index.md"),
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Changed, @"2026\post\index.md"),
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Changed, @"2026\post\index.md"),
            new(Kiji.Hosting.WatchedPathSource.Static, WatcherChangeTypes.Changed, "site.css"),
        ];

        var deduplicated = Kiji.Hosting.DevServer.DeduplicateChanges(events);

        Assert.Equal(2, deduplicated.Count);
        Assert.Equal(new Kiji.Hosting.WatchedChange(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Changed, @"2026\post\index.md"), deduplicated[0]);
        Assert.Equal(new Kiji.Hosting.WatchedChange(Kiji.Hosting.WatchedPathSource.Static, WatcherChangeTypes.Changed, "site.css"), deduplicated[1]);
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    private Task<(Uri BaseAddress, IAsyncDisposable DevServer)> StartServerAsync()
    {
        return StartServerAsync(new StringWriter());
    }

    private async Task<(Uri BaseAddress, IAsyncDisposable DevServer)> StartServerAsync(StringWriter logs)
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.txt"), "Hello World");

        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = _staticDir;

        var contentsDir = _contentsDir;
        var posts = builder.AddContentSource<Post>(_ =>
                [.. Directory.EnumerateFiles(contentsDir, "*.txt")
                    .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                    .Select(static file => TestArticleContents.CreatePost(
                        Path.GetFileNameWithoutExtension(file),
                        File.ReadAllText(file).Trim(),
                        "desc",
                        new DateOnly(2026, 1, 1),
                        null,
                        "Testing"))])
            .WithKey(static post => post.Slug);

        _app = builder.Build();
        TestArticleContents.MapSite(_app, posts);

        var reporter = new Kiji.Hosting.DevServerStatusReporter(logs, prefix: "kiji dev", useEmoji: true);
        var (devServer, web) = await _app.StartDevServerAsync(port: 0, CancellationToken.None, reporter);
        return (new Uri(web.Urls.First()), devServer);
    }
}
