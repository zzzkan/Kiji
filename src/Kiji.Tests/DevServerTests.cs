using System.Net;
using System.Net.WebSockets;
using System.Text;
using Kiji.Markdown;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerTests : IAsyncDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private readonly string _staticDir;
    private TestSiteHost? _app;
    private int _contentLoads;

    public DevServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DevServerTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "content");
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

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public async Task Serve_DefaultReporterUsesTheWebHostConfiguration(
        bool suppressEmoji, bool? redirected, bool color)
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "appsettings.Development.json"), """
            { "DOTNET_WATCH_SUPPRESS_EMOJIS": true, "NO_COLOR": true, "TERM": "dumb" }
            """);
        var builder = TestSiteFactory.CreateBuilder(
            new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")), outputPath: null);
        builder.UseSiteInfo(TestArticleContents.CreateSiteInfo());
        TestSiteAssets.Bind(builder.GetSitePaths(), _staticDir);
        _app = builder.BuildSite();
        using var output = new StringWriter();
        string[] args = [.. TestUrls.EphemeralPort,
            "--DOTNET_WATCH_SUPPRESS_EMOJIS", suppressEmoji ? "true" : "false",
            "--NO_COLOR", "false", "--TERM", "xterm"];

        var (server, web) = await _app.StartDevServerAsync(args, CancellationToken.None,
            statusOutput: output, isOutputRedirected: redirected);
        await using (server)
        {
            var log = output.ToString();
            Assert.Contains("Dev server started at", log, StringComparison.Ordinal);
            Assert.Contains(web.Urls.First(), log, StringComparison.Ordinal);
            Assert.Equal(!suppressEmoji, log.Contains("🚀", StringComparison.Ordinal));
            Assert.Equal(suppressEmoji, log.Contains("[Started]", StringComparison.Ordinal));
            Assert.Equal(color, log.Contains('\u001b'));
        }
    }

    [Fact]
    public async Task ContentRefresh_DoesNotCancelOrWaitForResponseTransmission()
    {
        using var logs = new StringWriter();
        var (baseAddress, server) = await StartServerAsync(logs);
        await using (server)
        {
            await using var body = new BlockingResponseStream();
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Path = "/";
            context.Response.Body = body;
            var response = server.HandlePageAsync(context);
            try
            {
                await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await server.ReloadAfterCodeUpdateAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(response.IsCompleted);
                using var client = CreateClient();
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(baseAddress)).StatusCode);
                body.Release.SetResult();
                await response.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains("</html>", Encoding.UTF8.GetString(body.ToArray()), StringComparison.Ordinal);
                await server.DisposeAsync();
                var stopped = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                await server.HandlePageAsync(stopped);
                Assert.Equal(503, stopped.Response.StatusCode);
            }
            finally
            {
                body.Release.TrySetResult();
                await response.WaitAsync(TimeSpan.FromSeconds(10));
            }
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
            Assert.Contains("href=\"https://example.com/blog/hello-world/\"", blogHtml, StringComparison.Ordinal);

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
            Assert.Contains("Watching static assets.", output, StringComparison.Ordinal);
            Assert.Contains("Watching content:", output, StringComparison.Ordinal);
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

            var untrustedQuery = await client.GetAsync(new Uri(baseAddress, "/blog?//evil.example"));

            Assert.Equal(HttpStatusCode.Found, untrustedQuery.StatusCode);
            Assert.Equal("/blog/?//evil.example", untrustedQuery.Headers.Location!.OriginalString);
            Assert.False(untrustedQuery.Headers.Location.IsAbsoluteUri);
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
            var html = await client.GetStringAsync(page);
            Assert.Contains("<h1>Hello World</h1>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://example.com/%E6%97%A5%E6%9C%AC/blog/%E6%97%A5%E6%9C%AC%E8%AA%9E/\"", html, StringComparison.Ordinal);
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

            await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.md"), "---\ntitle: Hello Updated\n---\n");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var buffer = new byte[64];
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, result.Count));

            var updatedHtml = await client.GetStringAsync(new Uri(baseAddress, "/blog/hello-world/"));
            Assert.Contains("<h1>Hello Updated</h1>", updatedHtml, StringComparison.Ordinal);

            var output = logs.ToString();
            Assert.Contains(output.Split('\n'), line => line.Contains("Content ", StringComparison.Ordinal)
                && line.Contains("hello-world.md", StringComparison.Ordinal));
            Assert.Contains("Reloaded 1 browser client(s).", output, StringComparison.Ordinal);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            Assert.Equal(WebSocketState.Closed, socket.State);
        }
    }

    [Theory]
    [InlineData("https://attacker.example", false)]
    [InlineData("null", false)]
    [InlineData("http://localhost:1", false)]
    [InlineData("http://attacker.example", true)]
    [InlineData(null, true)]
    public async Task Serve_LiveReloadRejectsUntrustedOriginsAndReboundHosts(string? origin, bool reboundHost)
    {
        var (baseAddress, devServer) = await StartServerAsync(new StringWriter(), TestArticleContents.CreateSiteInfoWithBasePath());
        await using (devServer)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/kiji/_kiji/reload"));
            request.Headers.Connection.Add("Upgrade");
            request.Headers.Upgrade.ParseAdd("websocket");
            request.Headers.Add("Sec-WebSocket-Version", "13");
            request.Headers.Add("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==");
            if (origin is not null) { request.Headers.Add("Origin", origin); }
            if (reboundHost) { request.Headers.Host = "attacker.example"; }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(reboundHost ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_LiveReloadAcceptsSameOriginBrowsers(bool localhost)
    {
        var (baseAddress, devServer) = await StartServerAsync(new StringWriter(), TestArticleContents.CreateSiteInfoWithBasePath());
        await using (devServer)
        {
            var page = new UriBuilder(baseAddress) { Host = localhost ? "localhost" : baseAddress.Host };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Origin", page.Uri.GetLeftPart(UriPartial.Authority));
            socket.Options.SetRequestHeader("Host", page.Uri.Authority);
            await socket.ConnectAsync(new UriBuilder(baseAddress) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, timeout.Token);

            Assert.Equal(WebSocketState.Open, socket.State);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_AllowedHostsCanBeConfigured(bool commandLine)
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "appsettings.Development.json"), """
            { "AllowedHosts": "dev.example" }
            """);
        var host = commandLine ? "cli.example" : "dev.example";
        string[] args = commandLine ? [.. TestUrls.EphemeralPort, "--AllowedHosts", host] : TestUrls.EphemeralPort;
        var (baseAddress, devServer) = await StartServerAsync(new StringWriter(), serverArgs: args);
        await using (devServer)
        {
            var page = new UriBuilder(baseAddress) { Host = host }.Uri;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Host", page.Authority);
            socket.Options.SetRequestHeader("Origin", page.GetLeftPart(UriPartial.Authority));
            await socket.ConnectAsync(new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri, timeout.Token);

            Assert.Equal(WebSocketState.Open, socket.State);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
    }

    [Fact]
    public async Task Serve_AssetDiscoveryServesNewFiles()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            await File.WriteAllTextAsync(Path.Combine(_staticDir, "new.css"), "body{}");
            using var client = CreateClient();
            Assert.Equal("body{}", await client.GetStringAsync(new Uri(baseAddress, "/new.css")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_RequiresManifest_AndAcceptsEmptyAssets(bool missing)
    {
        using var logs = new StringWriter();
        Task<(Uri BaseAddress, Kiji.Hosting.DevServer DevServer)> Start()
        {
            return StartServerAsync(logs, configureAssets: builder =>
            {
                var prefix = builder.GetSitePaths().AssetManifestBasePath!;
                var path = prefix + ".staticwebassets.runtime.json";
                if (missing) { File.Delete(path); }
                else { TestSiteAssets.WriteManifest(path, [], []); }
            });
        }

        if (missing)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(Start);
            Assert.Contains("is missing", error.Message, StringComparison.Ordinal);
            return;
        }

        var (baseAddress, devServer) = await Start();
        await using (devServer)
        {
            using var client = CreateClient();
            var html = await client.GetStringAsync(baseAddress);
            Assert.Contains("<title>Home - zzzkan.me</title>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Watching static assets.", logs.ToString(), StringComparison.Ordinal);
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
            Directory.Move(_contentsDir, Path.Combine(_testDir, "old-content"));
            Directory.CreateDirectory(_contentsDir);
            await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.md"), "---\ntitle: Replacement content\n---\n", timeout.Token);
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
            Assert.Contains(output.Split('\n'), line => line.Contains("Static asset ", StringComparison.Ordinal)
                && line.Contains("site.css", StringComparison.Ordinal));
            Assert.Contains("Reloaded 1 browser client(s).", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_CollocatedAssetsIgnoreRazorAndScopedCssSources()
    {
        var components = Path.Combine(_testDir, "Components");
        Directory.CreateDirectory(components);
        var script = Path.Combine(components, "Card.razor.js");
        await File.WriteAllTextAsync(script, "original");
        using var logs = new StringWriter();
        var (baseAddress, devServer) = await StartServerAsync(logs, configureAssets: builder =>
            TestSiteAssets.WriteManifest(builder.GetSitePaths().AssetManifestBasePath + ".staticwebassets.runtime.json",
                [new(script, "Components/Card.razor.js")], [components]));
        await using (devServer)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new UriBuilder(baseAddress) { Scheme = "ws", Path = "/_kiji/reload" }.Uri, timeout.Token);
            var buffer = new byte[64];
            var reload = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(components, "Card.razor"), "<p>changed</p>", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(components, "Card.razor.css"), "p { color: red; }", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(components, "unregistered.js"), "not served", timeout.Token);
            await Task.Delay(750, timeout.Token);
            Assert.False(reload.IsCompleted, logs.ToString());

            var temporary = Path.Combine(components, "save.tmp");
            await File.WriteAllTextAsync(temporary, "updated", timeout.Token);
            File.Move(temporary, script, overwrite: true);
            var received = await reload;
            Assert.Equal("reload", Encoding.UTF8.GetString(buffer, 0, received.Count));
            using var client = CreateClient();
            Assert.Equal("updated", await client.GetStringAsync(new Uri(baseAddress, "/Components/Card.razor.js")));
            Assert.DoesNotContain("Card.razor.css", logs.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("unregistered.js", logs.ToString(), StringComparison.Ordinal);
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
        var contentPath = Path.Combine(Path.GetTempPath(), "site", "content", "2026", "post", "index.md");
        var staticPath = Path.Combine(Path.GetTempPath(), "site", "wwwroot", "site.css");
        List<Kiji.Hosting.WatchedChange> events =
        [
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Created, contentPath, Path.Combine("content", "2026", "post", "index.md")),
            new(Kiji.Hosting.WatchedPathSource.Static, WatcherChangeTypes.Changed, contentPath, Path.Combine("content", "2026", "post", "index.md")),
            new(Kiji.Hosting.WatchedPathSource.Content, WatcherChangeTypes.Changed, contentPath, Path.Combine("content", "2026", "post", "index.md")),
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
        var siteFile = Path.Combine(displayRoot, "docs", "content", "index.md");
        var externalFile = Path.Combine(Path.GetTempPath(), "external", "authors.json");

        Assert.Equal(
            $".{Path.DirectorySeparatorChar}{Path.Combine("docs", "content", "index.md")}",
            Kiji.Hosting.DevServer.GetDisplayPath(siteFile, displayRoot));
        Assert.Equal(
            Path.GetFullPath(externalFile),
            Kiji.Hosting.DevServer.GetDisplayPath(externalFile, displayRoot));
    }

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

            var queried = await client.GetStringAsync(new Uri(baseAddress, "/kiji/blog/hello-world/?preview=1"));
            Assert.Contains("href=\"https://example.com/kiji/blog/hello-world/\"", queried, StringComparison.Ordinal);
            using var missing = await client.GetAsync(new Uri(baseAddress, "/kiji/missing/?preview=1"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("href=\"https://example.com/kiji/404.html\"", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var post = await client.GetAsync(new Uri(baseAddress, "/kiji/blog/hello-world/"));
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains(
                "<h1>Hello World</h1>",
                await post.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            Assert.Equal(
                HttpStatusCode.Found,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/blog/hello-world"))).StatusCode);

            var root = await client.GetAsync(new Uri(baseAddress, "/"));
            Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
            Assert.Null(root.Headers.Location);

            var rootWithUntrustedQuery = await client.GetAsync(new Uri(baseAddress, "/?//evil.example"));
            Assert.Equal(HttpStatusCode.NotFound, rootWithUntrustedQuery.StatusCode);
            Assert.Null(rootWithUntrustedQuery.Headers.Location);

            await File.WriteAllTextAsync(Path.Combine(_staticDir, "site.css"), "body{}");
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/site.css"))).StatusCode);

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

    [Fact]
    public async Task Serve_ExceptionPageForNonHtmlRequestStaysPlainText()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "broken.md"), "---\ntitle: [unclosed\n---\n");
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/"));
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            var text = await response.Content.ReadAsStringAsync();
            Assert.Contains("broken.md", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_PageRoutesAreCaseSensitiveLikeStaticHosts()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();
            using var page = await client.GetAsync(new Uri(baseAddress, "/blog/hello-world/"));
            using var otherCase = await client.GetAsync(new Uri(baseAddress, "/Blog/Hello-World/"));

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, otherCase.StatusCode);
        }
    }

    [Fact]
    public async Task Serve_ExceptionPageKeepsLiveReloadUnderTheBasePath()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "broken.md"), "---\ntitle: [unclosed\n---\n");
        var (baseAddress, devServer) = await StartServerAsync(new StringWriter(), TestArticleContents.CreateSiteInfoWithBasePath());
        await using (devServer)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/kiji/"));
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("broken.md", html, StringComparison.Ordinal);
            Assert.Contains("<script src=\"/kiji/_kiji/livereload.js\" defer></script>", html, StringComparison.Ordinal);
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Serve_WebHostUsesTheSiteEnvironmentAndLogging()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(Path.Combine(_testDir, "site")),
            runtimeMetadata: null, hostConfiguration: TestSiteFactory.HostConfiguration(("DOTNET_ENVIRONMENT", "Staging")));
        builder.UseSiteInfo(TestArticleContents.CreateSiteInfo());
        var logs = new RecordingLoggerProvider();
        builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information);
        _app = builder.BuildSite();
        var (server, web) = await _app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None,
            new Kiji.Hosting.DevServerStatusReporter(TextWriter.Null, "kiji", useEmoji: false));
        await using (server)
        {
            Assert.Equal("Staging", web.Environment.EnvironmentName);
            Assert.Equal(builder.Environment.ContentRootPath, web.Environment.ContentRootPath);

            using var client = CreateClient();
            using var response = await client.GetAsync(new Uri(new Uri(web.Urls.First()), "/missing/"));

            Assert.Contains(logs.Entries, static entry => entry.Category == "Microsoft.AspNetCore.Hosting.Diagnostics"
                && entry.Message.StartsWith("Request starting", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Serve_MultipleMarkdownRoots_ReloadsAfterEditsAndRootReplacement(bool absolute)
    {
        var inputs = Path.Combine(_testDir, "inputs");
        var posts = Path.Combine(inputs, "posts");
        var notes = Path.Combine(inputs, "notes");
        Directory.CreateDirectory(posts);
        Directory.CreateDirectory(notes);
        await File.WriteAllTextAsync(Path.Combine(posts, "post.md"), "---\ntitle: Post\n---\n\nOriginal body");
        await File.WriteAllTextAsync(Path.Combine(notes, "note.md"), "---\ntitle: Original note\n---\n");
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(Path.Combine(_testDir, "site")), outputPath: null);
        builder.UseSiteInfo(TestArticleContents.CreateSiteInfoWithBasePath());
        string SourcePath(string path)
        {
            return absolute ? path : Path.GetRelativePath(builder.GetSitePaths().ProjectDirectory, path);
        }
        builder.AddMarkdownContent<FrontMatter>(options => options.Directory = SourcePath(posts));
        builder.AddMarkdownContent<FrontMatter, ScopedNote>(ScopedNote.Create,
            options => options.Directory = SourcePath(notes));
        builder.AddPageParameters<MarkdownPostTestPage>(services => services
            .GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>()
            .Select(post => new { Slug = "post", ContentKey = post.Path }));
        builder.AddPageParameters<ScopedNotesIndexPage>(_ => [new { Kind = "all" }]);
        _app = builder.BuildSite();
        using var logs = new StringWriter();
        var (server, web) = await _app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None,
            new Kiji.Hosting.DevServerStatusReporter(logs, "kiji", useEmoji: false));
        await using (server)
        {
            var address = new Uri(web.Urls.First());
            using var client = CreateClient();
            var postPage = new Uri(address, "/kiji/md/post/");
            var notePage = new Uri(address, "/kiji/notes/all/");
            Assert.Contains("Original body", await client.GetStringAsync(postPage), StringComparison.Ordinal);
            Assert.Contains("Original note", await client.GetStringAsync(notePage), StringComparison.Ordinal);
            using var socket = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/kiji/_kiji/reload" }.Uri, timeout.Token);
            var reload = socket.ReceiveAsync(new ArraySegment<byte>(new byte[64]), timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(inputs, "ignored.md"), "unregistered sibling", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(_contentsDir, "ignored.md"), "unused convention", timeout.Token);
            await Task.Delay(750, timeout.Token);
            Assert.False(reload.IsCompleted, logs.ToString());

            await File.WriteAllTextAsync(Path.Combine(posts, "post.md"), "---\ntitle: Post\n---\n\nEdited body", timeout.Token);
            await reload;
            Assert.Contains("Edited body", await client.GetStringAsync(postPage), StringComparison.Ordinal);

            await File.WriteAllTextAsync(Path.Combine(notes, "note.md"), "---\ntitle: Edited note\n---\n", timeout.Token);
            await socket.ReceiveAsync(new byte[64], timeout.Token);
            Assert.Contains("Edited note", await client.GetStringAsync(notePage), StringComparison.Ordinal);

            Directory.Move(posts, Path.Combine(inputs, "old-posts"));
            Directory.Move(notes, Path.Combine(inputs, "old-notes"));
            Directory.CreateDirectory(posts);
            Directory.CreateDirectory(notes);
            await File.WriteAllTextAsync(Path.Combine(posts, "post.md"), "---\ntitle: Post\n---\n\nReplacement body", timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(notes, "note.md"), "---\ntitle: Replacement note\n---\n", timeout.Token);
            await socket.ReceiveAsync(new byte[64], timeout.Token);
            Assert.Contains("Replacement body", await client.GetStringAsync(postPage), StringComparison.Ordinal);
            Assert.Contains("Replacement note", await client.GetStringAsync(notePage), StringComparison.Ordinal);

            await File.WriteAllTextAsync(Path.Combine(posts, "post.md"), "---\ntitle: Post\n---\n\nSubsequent edit", timeout.Token);
            await socket.ReceiveAsync(new byte[64], timeout.Token);
            Assert.Contains("Subsequent edit", await client.GetStringAsync(postPage), StringComparison.Ordinal);
        }
    }

    private Task<(Uri BaseAddress, Kiji.Hosting.DevServer DevServer)> StartServerAsync()
    {
        return StartServerAsync(new StringWriter());
    }

    private async Task<(Uri BaseAddress, Kiji.Hosting.DevServer DevServer)> StartServerAsync(
        StringWriter logs,
        SiteInfo? site = null,
        string? additionalSlug = null,
        Action<HostApplicationBuilder>? configureAssets = null,
        string[]? serverArgs = null)
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.md"), "---\ntitle: Hello World\n---\n");

        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")), outputPath: null);
        builder.UseSiteInfo(site ?? TestArticleContents.CreateSiteInfo());
        TestSiteAssets.Bind(builder.GetSitePaths(), _staticDir);
        configureAssets?.Invoke(builder);

        var contentsDir = _contentsDir;
        builder.AddMarkdownContent<FrontMatter, Post>(content =>
            {
                Interlocked.Increment(ref _contentLoads);
                return TestArticleContents.CreatePost(
                        Path.GetFileNameWithoutExtension(content.Path),
                        content.FrontMatter.Title!,
                        "desc",
                        new DateOnly(2026, 1, 1),
                        null,
                        "Testing");
            }, options => options.Directory = contentsDir);

        TestArticleContents.MapSite(builder);
        if (additionalSlug is not null)
        {
            builder.AddPageParameters<Kiji.Tests.TestSite.Pages.PostPage>(_ => [new { Slug = additionalSlug, ContentKey = "hello-world.md" }]);
        }
        _app = builder.BuildSite();

        var reporter = new Kiji.Hosting.DevServerStatusReporter(logs, prefix: "kiji", useEmoji: true);
        var (devServer, web) = await _app.StartDevServerAsync(serverArgs ?? TestUrls.EphemeralPort, CancellationToken.None, reporter);
        return (new Uri(web.Urls.First()), devServer);
    }
}
