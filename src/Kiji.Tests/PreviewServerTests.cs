using System.Net;
using Kiji.Tests.TestSite;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Integration tests for the preview server, which serves the built output with
/// production-equivalent trailing-slash, 404, and base-path semantics.
/// </summary>
public sealed class PreviewServerTests : IAsyncDisposable
{
    private readonly string _testDir;
    private readonly string _contentsDir;
    private KijiApp? _app;

    public PreviewServerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"PreviewServerTests_{Guid.NewGuid():N}");
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
    public async Task Preview_ServesBuiltOutputWithTrailingSlashAndNotFoundSemantics()
    {
        var (baseAddress, web) = await StartPreviewAsync(TestArticleContents.CreateSiteInfo());
        await using (web)
        {
            using var client = CreateClient();

            var home = await client.GetAsync(new Uri(baseAddress, "/"));
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains(
                "<title>Home - zzzkan.me</title>",
                await home.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            // Extensionless paths resolve to route/index.html without a redirect.
            var post = await client.GetAsync(new Uri(baseAddress, "/blog/hello-world"));
            Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            Assert.Contains(
                "<h1>Hello World</h1>",
                await post.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            var missing = await client.GetAsync(new Uri(baseAddress, "/nope/"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains(
                "404",
                await missing.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Preview_WithBasePath_ServesUnderThePrefixAndRejectsOutsideIt()
    {
        var (baseAddress, web) = await StartPreviewAsync(TestArticleContents.CreateSiteInfoWithBasePath());
        await using (web)
        {
            using var client = CreateClient();

            var home = await client.GetAsync(new Uri(baseAddress, "/kiji/"));
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains(
                "<title>Home - zzzkan.me</title>",
                await home.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);

            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/blog/hello-world"))).StatusCode);

            var root = await client.GetAsync(new Uri(baseAddress, "/"));
            Assert.Equal(HttpStatusCode.Found, root.StatusCode);
            Assert.Equal("/kiji/", root.Headers.Location!.ToString());

            // Static assets resolve under the prefix, which is where Site.Path points them.
            Assert.Equal(
                HttpStatusCode.OK,
                (await client.GetAsync(new Uri(baseAddress, "/kiji/css/app.css"))).StatusCode);

            // The deployed site serves nothing here, so neither does preview.
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(new Uri(baseAddress, "/blog/hello-world"))).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.GetAsync(new Uri(baseAddress, "/css/app.css"))).StatusCode);

            var missing = await client.GetAsync(new Uri(baseAddress, "/kiji/nope/"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    }

    private async Task<(Uri BaseAddress, WebApplication Web)> StartPreviewAsync(SiteInfo site)
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = site;
        builder.Paths.Root = _testDir;
        builder.Paths.Content = _contentsDir;
        builder.Paths.Static = TestSitePaths.StaticDirectory;

        IReadOnlyList<Post> items =
        [
            TestArticleContents.CreatePost(
                "hello-world",
                "Hello World",
                "Hello world description",
                new DateOnly(2026, 3, 18),
                null,
                "Testing"),
        ];
        var posts = builder.AddContentSource<Post>(_ => items).WithKey(static post => post.Slug);

        _app = builder.Build();
        TestArticleContents.MapSite(_app, posts);
        await _app.BuildSiteAsync();

        var web = await _app.StartPreviewServerAsync(port: 0, CancellationToken.None);
        return (new Uri(web.Urls.First()), web);
    }
}
