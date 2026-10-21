using System.Text.Json;
using Kiji.Generation;
using Kiji.Tests.TestSite.PageCaching;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class PageReuseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-page-reuse-{Guid.NewGuid():N}");

    [Fact]
    public async Task AlwaysRender_PropagatesThroughNestedDynamicComponents_WithoutAffectingOtherPages()
    {
        var probe = new RenderPolicyProbe();
        for (var round = 1; round <= 3; round++)
        {
            probe.Value = $"value-{round}";
            // A fresh site cannot rely on an in-memory record of previously activated children.
            await using var site = CreateSite(probe);
            site.AddPages<AlwaysRenderPage>(_ => [new { Id = "one" }, new { Id = "two" }]);
            site.AddPages<RenderPolicyPage>(_ =>
            [
                new { Id = "nested", IncludeVolatile = true },
                new { Id = "stable", IncludeVolatile = false },
            ]);
            if (round == 3) { Directory.Delete(Path.Combine(_root, "dist"), recursive: true); }
            await site.PublishAsync("dist");

            Assert.Equal(round, probe.Renders["always-one"]);
            Assert.Equal(round, probe.Renders["always-two"]);
            Assert.Equal(round, probe.Renders["nested"]);
            Assert.Equal(round, probe.Renders["child"]);
            Assert.Equal(1, probe.Renders["stable"]);
            Assert.Contains(probe.Value, ReadPage("always/one"), StringComparison.Ordinal);
            Assert.Contains(probe.Value, ReadPage("policy/nested"), StringComparison.Ordinal);
            Assert.Contains("value-1", ReadPage("policy/stable"), StringComparison.Ordinal);
        }

        var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(_root, ".kiji", "cache", "manifest.json")),
            BuildManifestJsonContext.Default.BuildManifest)!;
        Assert.All(manifest.Pages.Where(page => page.RoutePath != "/policy/stable/"),
            page => Assert.Equal(0, page.HtmlLength));
        Assert.True(manifest.Pages.Single(page => page.RoutePath == "/policy/stable/").HtmlLength > 0);
    }

    [Fact]
    public async Task AlwaysRender_OnAnOuterLayout_DisablesReuseForEveryPageUsingIt()
    {
        var probe = new RenderPolicyProbe();
        for (var round = 1; round <= 2; round++)
        {
            probe.Value = $"layout-value-{round}";
            await using var site = CreateSite(probe);
            site.UseDefaultLayout<RenderPolicyLayout>();
            site.AddPages<RenderPolicyPage>(_ => [new { Id = "one" }, new { Id = "two" }]);
            await site.PublishAsync("dist");
            Assert.Equal(round, probe.Renders["one"]);
            Assert.Equal(round, probe.Renders["two"]);
            Assert.Equal(round * 2, probe.Renders["layout"]);
            Assert.Contains(probe.Value, ReadPage("policy/one"), StringComparison.Ordinal);
            Assert.Contains(probe.Value, ReadPage("policy/two"), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForcedRendering_RefreshesTheCacheForTheNextNormalPublish(bool forceFromMsBuild)
    {
        var probe = new RenderPolicyProbe();
        for (var round = 1; round <= 4; round++)
        {
            probe.Value = $"value-{round}";
            await using var site = CreateSite(probe);
            site.AddPages<RenderPolicyPage>(_ => [new { Id = "one" }]);
            var force = round == 3;
            site.ReusePages = forceFromMsBuild || !force;
            var exitCode = await site.RunAsync(name => name switch
            {
                "KIJI_OUTPUT" => "dist",
                "KIJI_FORCE" when forceFromMsBuild && force => "true",
                _ => null,
            }, CancellationToken.None);
            Assert.Equal(0, exitCode);
            Assert.Equal(round < 3 ? 1 : 2, probe.Renders["one"]);
            Assert.Contains(round < 3 ? "value-1" : "value-3", ReadPage("policy/one"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RemovingTheVolatileChild_AllowsSubsequentPublishesToReuseThePage()
    {
        var probe = new RenderPolicyProbe();
        for (var round = 1; round <= 3; round++)
        {
            await using var site = CreateSite(probe);
            site.AddPages<RenderPolicyPage>(_ => [new { Id = "one", IncludeVolatile = round == 1 }]);
            await site.PublishAsync("dist");
            Assert.Equal(Math.Min(round, 2), probe.Renders["one"]);
            Assert.Equal(1, probe.Renders["child"]);
        }
    }

    private StaticSite CreateSite(RenderPolicyProbe probe)
    {
        var site = StaticSite.Create([], new SiteExecutionPaths(_root));
        site.Info = TestArticleContents.CreateSiteInfo();
        site.UseServices(services => services.AddSingleton(probe));
        return site;
    }

    private string ReadPage(string path) => File.ReadAllText(Path.Combine(_root, "dist", path, "index.html"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
}
