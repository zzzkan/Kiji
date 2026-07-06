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
    private KijiApp? _app;

    public DevServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DevServerTests_{Guid.NewGuid():N}");
        _contentsDir = Path.Combine(_testDir, "contents");
        Directory.CreateDirectory(_contentsDir);
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
        var (baseAddress, devServer) = await StartServerAsync();
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
        }
    }

    [Fact]
    public async Task Serve_RedirectsMissingTrailingSlashLikeProduction()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var response = await client.GetAsync(new Uri(baseAddress, "/blog"));

            Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
            Assert.Equal("/blog/", response.Headers.Location?.OriginalString);
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
    public async Task Serve_ServesFeedAndSitemapOnDemand()
    {
        var (baseAddress, devServer) = await StartServerAsync();
        await using (devServer)
        {
            using var client = CreateClient();

            var feed = await client.GetStringAsync(new Uri(baseAddress, "/feed.xml"));
            Assert.Contains("<![CDATA[Hello World]]>", feed, StringComparison.Ordinal);

            var sitemap = await client.GetStringAsync(new Uri(baseAddress, "/sitemap.xml"));
            Assert.Contains("https://example.com/blog/hello-world/", sitemap, StringComparison.Ordinal);
            Assert.DoesNotContain("404.html", sitemap, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Serve_ContentChangeBroadcastsReloadAndServesUpdatedContent()
    {
        var (baseAddress, devServer) = await StartServerAsync();
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
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    private async Task<(Uri BaseAddress, IAsyncDisposable DevServer)> StartServerAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentsDir, "hello-world.txt"), "Hello World");

        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = GetStaticDirectory();

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
        _app.MapFeed(posts, static post => new FeedItem(post.Title, post.Description, post.CreatedAt));

        var (devServer, web) = await _app.StartDevServerAsync(port: 0, CancellationToken.None);
        return (new Uri(web.Urls.First()), devServer);
    }

    private static string GetStaticDirectory()
    {
        return TestSitePaths.StaticDirectory;
    }
}
