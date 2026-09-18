using System.Net;
using System.Net.WebSockets;
using System.Text;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerTests : IAsyncDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _staticDir;
    private StaticSite? _app;
    private int _contentLoads;

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

            foreach (var path in new[] { "/Blog/", "/no-such-page/", "/missing.html" })
            {
                using var missing = await client.GetAsync(new Uri(baseAddress, path));
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.Equal("text/html", missing.Content.Headers.ContentType?.MediaType);
            }
            using var notFound = await client.GetAsync(new Uri(baseAddress, "/404.html"));
            Assert.Equal(HttpStatusCode.OK, notFound.StatusCode);
            Assert.Contains("/_kiji/livereload.js", await notFound.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var output = logs.ToString();
            Assert.Contains("Dev server started at", output, StringComparison.Ordinal);
            Assert.Contains("Watching content:", output, StringComparison.Ordinal);
            Assert.Contains("Watching static assets:", output, StringComparison.Ordinal);
            Assert.Contains("Watching build input:", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_RedirectsMissingTrailingSlashPreservingQuery()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/blog?tag=a%20b"));

            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("/blog/?tag=a%20b", response.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task Serve_UnicodeRouteAndBasePathMatchBrowserRequests()
    {
        var source = TestArticleContents.CreateSiteInfo();
        var site = new SiteInfo
        {
            BaseUrl = new Uri("https://example.com/日本/"),
            Name = source.Name,
            Description = source.Description,
            Language = source.Language,
            Author = source.Author,
        };
        var (baseAddress, devServer) = await StartServerAsync(new StringWriter(), site, "日本語");
        await using (devServer)
        {
            using var client = CreateClient();
            var page = new Uri(baseAddress, "/日本/blog/日本語/");
            Assert.Contains("<h1>Hello World</h1>", await client.GetStringAsync(page), StringComparison.Ordinal);
            var redirect = await client.GetAsync(new Uri(baseAddress, "/日本/blog/日本語?q=1"));
            Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
            Assert.Equal("/%E6%97%A5%E6%9C%AC/blog/%E6%97%A5%E6%9C%AC%E8%AA%9E/?q=1", redirect.Headers.Location!.OriginalString);
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
            Assert.Contains(
                $"Content changed: {Path.Combine(_contentsDir, "hello-world.txt")}",
                output,
                StringComparison.Ordinal);
            Assert.Contains("Reloaded 1 browser client(s).", output, StringComparison.Ordinal);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            Assert.Equal(WebSocketState.Closed, socket.State);
        }
    }

    [Fact]
    public async Task Serve_DeclaredBuildInputChangeReloadsContent()
    {
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs);
        await using (devServer)
        {
            using var client = CreateClient();
            var page = new Uri(baseAddress, "/blog/hello-world/");
            Assert.Contains("<h1>Hello World</h1>", await client.GetStringAsync(page), StringComparison.Ordinal);
            using var socket = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(_testDir, "title.txt"), "External setting updated", timeout.Token);
            await socket.ReceiveAsync(new byte[64], timeout.Token);
            Assert.Contains("<h1>External setting updated</h1>", await client.GetStringAsync(page), StringComparison.Ordinal);
            Assert.Contains(
                $"Build input changed: {Path.Combine(_testDir, "title.txt")}",
                logs.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_StaticDirectoryCreatedDuringStartupServesNewFiles()
    {
        Directory.Delete(_staticDir);
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            await File.WriteAllTextAsync(Path.Combine(_staticDir, "new.css"), "body{}");
            using var client = CreateClient();
            Assert.Equal("body{}", await client.GetStringAsync(new Uri(baseAddress, "/new.css")));
        }
    }

    [Fact]
    public async Task Serve_ReplacedContentDirectoryStillReceivesChanges()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();
            var page = new Uri(baseAddress, "/blog/hello-world/");
            await client.GetStringAsync(page);
            using var socket = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri, timeout.Token);
            Directory.Move(_contentsDir, Path.Combine(_testDir, "old-contents"));
            Directory.CreateDirectory(_contentsDir);
            await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.txt"), "Replacement content", timeout.Token);
            await socket.ReceiveAsync(new byte[64], timeout.Token);
            Assert.Contains("<h1>Replacement content</h1>", await client.GetStringAsync(page), StringComparison.Ordinal);
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
            using var client = CreateClient();
            var page = new Uri(baseAddress, "/blog/hello-world/");
            await client.GetStringAsync(page);
            var loads = _contentLoads;
            using var socket = new ClientWebSocket();
            var wsUri = new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri;
            await socket.ConnectAsync(wsUri, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(_staticDir, "site.css"), "body { color: red; }");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));

            await client.GetStringAsync(page);
            Assert.Equal(loads, _contentLoads);
            Assert.Contains("color: red", await client.GetStringAsync(new Uri(baseAddress, "/site.css")), StringComparison.Ordinal);

            var output = logs.ToString();
            Assert.Contains(
                $"Static asset changed: {Path.Combine(_staticDir, "site.css")}",
                output,
                StringComparison.Ordinal);
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

            await Kiji.Hosting.DevServer.NotifyCodeUpdated();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    [Fact]
    public void DeduplicateChanges_CollapsesRepeatedEventsPerFileKeepingLatestChangeType()
    {
        var contentPath = Path.Combine(Path.GetTempPath(), "site", "contents", "2026", "post", "index.md");
        var staticPath = Path.Combine(Path.GetTempPath(), "site", "wwwroot", "site.css");
        List<Kiji.Hosting.WatchedChange> events =
        [
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Created, contentPath, Path.Combine("contents", "2026", "post", "index.md")),
            new(Kiji.Hosting.WatchedPathSource.BuildInput, WatcherChangeTypes.Changed, contentPath, Path.Combine("contents", "2026", "post", "index.md")),
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Changed, contentPath, Path.Combine("contents", "2026", "post", "index.md")),
            new(Kiji.Hosting.WatchedPathSource.Static, WatcherChangeTypes.Changed, staticPath, Path.Combine("wwwroot", "site.css")),
        ];

        var deduplicated = Kiji.Hosting.DevServer.DeduplicateChanges(events);

        Assert.Equal(2, deduplicated.Count);
        Assert.Equal(Kiji.Hosting.WatchedPathSource.Content, deduplicated[0].Source);
        Assert.Equal(WatcherChangeTypes.Changed, deduplicated[0].ChangeType);
        Assert.Equal(contentPath, deduplicated[0].FullPath);
        Assert.Equal(Kiji.Hosting.WatchedPathSource.Static, deduplicated[1].Source);
        Assert.Equal(staticPath, deduplicated[1].FullPath);
    }

    [Fact]
    public void GetDisplayPath_UsesWorkingDirectoryStylePathAndKeepsExternalPathAbsolute()
    {
        var displayRoot = Path.Combine(Path.GetTempPath(), "workspace");
        var siteFile = Path.Combine(displayRoot, "docs", "contents", "index.md");
        var externalFile = Path.Combine(Path.GetTempPath(), "external", "authors.json");

        Assert.Equal(
            $".{Path.DirectorySeparatorChar}{Path.Combine("docs", "contents", "index.md")}",
            Kiji.Hosting.DevServer.GetDisplayPath(siteFile, displayRoot));
        Assert.Equal(
            Path.GetFullPath(externalFile),
            Kiji.Hosting.DevServer.GetDisplayPath(externalFile, displayRoot));
    }

    /// <summary>
    /// A site published under a sub-path must be browsable at that sub-path locally,
    /// and must serve nothing outside it — otherwise a link that omits
    /// <see cref="SiteInfo.BaseUrl"/>'s path works in dev and 404s only once deployed.
    /// </summary>
    [Fact]
    public async Task Serve_WithBasePath_ServesUnderThePrefixAndRejectsOutsideIt()
    {
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs, TestArticleContents.CreateSiteInfoWithBasePath());
        await using (devServer)
        {
            using var client = CreateClient();

            var home = await client.GetAsync(new Uri(baseAddress, "/kiji/"));
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains(
                "<title>Home - zzzkan.me</title>",
                await home.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            var script = await client.GetStringAsync(new Uri(baseAddress, "/kiji/_kiji/livereload.js"));
            Assert.Contains("WebSocket", script, StringComparison.Ordinal);
            Assert.Contains("/kiji/_kiji/livereload.js", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var prefix = await client.GetAsync(new Uri(baseAddress, "/kiji"));
            Assert.Equal(HttpStatusCode.Found, prefix.StatusCode);
            Assert.Equal("/kiji/", prefix.Headers.Location!.OriginalString);

            var post = await client.GetAsync(new Uri(baseAddress, "/kiji/blog/hello-world/"));
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains(
                "<h1>Hello World</h1>",
                await post.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Trailing-slash resolution still works underneath the prefix.
            Assert.Equal(
                HttpStatusCode.Found,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/blog/hello-world"))).StatusCode);

            // The server root redirects rather than 404s, so the developer lands somewhere useful.
            var root = await client.GetAsync(new Uri(baseAddress, "/"));
            Assert.Equal(HttpStatusCode.Found, root.StatusCode);
            Assert.Equal("/kiji/", root.Headers.Location!.ToString());

            // Static assets resolve under the prefix exposed by Site.BaseUrl.AbsolutePath.
            await File.WriteAllTextAsync(Path.Combine(_staticDir, "site.css"), "body{}");
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/site.css"))).StatusCode);

            // Anything else outside the prefix is a 404, exactly as after deployment.
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(new Uri(baseAddress, "/blog/hello-world/"))).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(new Uri(baseAddress, "/site.css"))).StatusCode);

            Assert.Contains(
                $"Dev server started at {new Uri(baseAddress, "/kiji/")}",
                logs.ToString(),
                StringComparison.Ordinal);
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    private Task<(Uri BaseAddress, IAsyncDisposable DevServer)> StartServerAsync()
    {
        return StartServerAsync(new StringWriter());
    }

    private async Task<(Uri BaseAddress, IAsyncDisposable DevServer)> StartServerAsync(
        StringWriter logs,
        SiteInfo? site = null,
        string? additionalSlug = null)
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.txt"), "Hello World");

        var app = StaticSite.Create([]);
        app.Info = site ?? TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _testDir;
        app.Paths.ContentDirectory = _contentsDir;
        app.Paths.StaticDirectory = _staticDir;

        var contentsDir = _contentsDir;
        var settingsPath = Path.Combine(_testDir, "title.txt");
        app.AddBuildInput(settingsPath);
        app.UseContentSource<Post>(_ =>
            {
                Interlocked.Increment(ref _contentLoads);
                return [.. Directory.EnumerateFiles(contentsDir, "*.txt")
                    .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                    .Select(file => TestArticleContents.CreatePost(
                        Path.GetFileNameWithoutExtension(file),
                        File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : File.ReadAllText(file).Trim(),
                        "desc",
                        new DateOnly(2026, 1, 1),
                        null,
                        "Testing"))];
            });

        _app = app;
        TestArticleContents.MapSite(_app);
        if (additionalSlug is not null)
        {
            _app.AddPages<Kiji.Tests.TestSite.Pages.PostPage>(_ => [new { Slug = additionalSlug, ContentKey = "0" }]);
        }

        var reporter = new Kiji.Hosting.DevServerStatusReporter(logs, prefix: "kiji", useEmoji: true);
        var (devServer, web) = await _app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None, reporter);
        return (new Uri(web.Urls.First()), devServer);
    }
}
