using System.Text.Json;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticAssetTests : IDisposable
{
    private static readonly string[] PageKinds = ["assets", "plain", "importmap", "custom"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-assets-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    [Theory]
    [InlineData("app.css")]
    [InlineData("app.js")]
    [InlineData("module.mjs")]
    [InlineData("font.woff2")]
    [InlineData("image.png")]
    [InlineData("image.svg")]
    [InlineData("data.json")]
    [InlineData("data.xml")]
    [InlineData("download.pdf")]
    [InlineData("module.wasm")]
    [InlineData("static.html")]
    [InlineData("日本語/space #%.svg")]
    public async Task Publish_RendersSdkUrlAndMaterializesItsExactBytes(string path)
    {
        var hashed = path.Insert(path.LastIndexOf('.'), ".hash");
        var log = new AssetRenderLog();
        await using var site = CreateSite(log, path);
        WriteAssets(site, path, hashed);

        await site.PublishAsync(Path.Combine(_root, "dist"));

        var html = await File.ReadAllTextAsync(Path.Combine(_root, "dist", "assets", "assets", "index.html"));
        var url = "/kiji/" + string.Join('/', hashed.Split('/').Select(Uri.EscapeDataString));
        Assert.Contains($"href=\"{url}\"", html, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_root, "wwwroot", path)),
            File.ReadAllBytes(Path.Combine(_root, "dist", hashed)));
    }

    [Fact]
    public async Task Publish_DefaultImportMapUsesSdkResourcesAndExplicitMapOverridesIt()
    {
        await using var site = CreateSite(new AssetRenderLog());
        WriteAssets(site, "app.js", "app.first.js");
        await site.PublishAsync(Path.Combine(_root, "dist"));

        var html = await ReadPage("importmap");
        Assert.Contains("type=\"importmap\"", html, StringComparison.Ordinal);
        Assert.Contains("app.first.js", html, StringComparison.Ordinal);
        Assert.Contains("sha256-", html, StringComparison.Ordinal);
        var custom = await ReadPage("custom");
        Assert.Contains("/custom/module.js", custom, StringComparison.Ordinal);
        Assert.DoesNotContain("app.first.js", custom, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_AssetChangesInvalidateConsumersIncludingPreviouslyMissingLookup()
    {
        var log = new AssetRenderLog();
        await using var site = CreateSite(log);
        TestSiteAssets.Bind(site, Path.Combine(_root, "wwwroot"));
        var output = Path.Combine(_root, "dist");
        await site.PublishAsync(output);
        await site.PublishAsync(output);
        Assert.All(log.Counts.Values, count => Assert.Equal(1, count));

        WriteAssets(site, "app.js", "app.first.js");
        await site.PublishAsync(output);
        Assert.Equal(2, log.Counts["assets"]);
        Assert.Equal(2, log.Counts["importmap"]);
        Assert.Equal(1, log.Counts["plain"]);
        Assert.Contains("app.first.js", await ReadPage("assets"), StringComparison.Ordinal);

        WriteAssets(site, "app.js", "app.second.js");
        await site.PublishAsync(output);
        Assert.Equal(3, log.Counts["assets"]);
        Assert.Equal(3, log.Counts["importmap"]);
        Assert.Equal(1, log.Counts["plain"]);
        Assert.False(File.Exists(Path.Combine(output, "app.first.js")));
        Assert.Contains("app.second.js", await ReadPage("assets"), StringComparison.Ordinal);
        await site.PublishAsync(output);
        Assert.Equal(3, log.Counts["assets"]);
    }

    [Fact]
    public async Task Publish_RejectsAnAssetChangedSinceTheSdkResolvedItsUrl()
    {
        await using var site = CreateSite(new AssetRenderLog());
        WriteAssets(site, "app.js", "app.first.js");
        await File.WriteAllTextAsync(Path.Combine(_root, "wwwroot", "app.js"), "changed after SDK resolution");

        var exception = await Assert.ThrowsAsync<IOException>(() => site.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("Rebuild the site", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "dist", "assets", "assets", "index.html")));
    }

    private StaticSite CreateSite(AssetRenderLog log, string path = "app.js")
    {
        var site = StaticSite.Create([], new SiteExecutionPaths(_root, Path.Combine(_root, "obj", "site")));
        site.Info = TestArticleContents.CreateSiteInfoWithBasePath();
        site.UseContentSource<AssetRenderLog>("render-log", _ => [new("log", log, "stable")]);
        site.AddPages<AssetPage>(_ => PageKinds
            .Select(kind => new { Kind = kind, Path = path }));
        return site;
    }

    private void WriteAssets(StaticSite site, string path, string hashed)
    {
        var directory = Path.Combine(_root, "wwwroot");
        var source = Path.Combine(directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "asset bytes " + hashed);
        TestSiteAssets.Bind(site, directory);
        var endpointsPath = site.Paths.AssetManifestBasePath + ".staticwebassets.publish.endpoints.json";
        File.WriteAllText(endpointsPath, JsonSerializer.Serialize(new
        {
            Endpoints = new[]
            {
                new
                {
                    Route = hashed, AssetFile = path, Selectors = Array.Empty<object>(),
                    EndpointProperties = new[]
                    {
                        new { Name = "label", Value = path },
                        new { Name = "integrity", Value = "sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))) },
                    },
                },
            },
        }));
    }

    private Task<string> ReadPage(string kind) => File.ReadAllTextAsync(Path.Combine(_root, "dist", "assets", kind, "index.html"));
}
