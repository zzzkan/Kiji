using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class AppConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"KijiConfiguration-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("publish")]
    [InlineData("dev")]
    public async Task MissingSite_FailsBeforeCreatingOutput_AndCanBeCorrected(string entry)
    {
        await using var app = StaticSite.Create([]);
        app.Paths.RootDirectory = _root;
        var output = Path.Combine(_root, "output");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            switch (entry)
            {
                case "publish": await app.PublishAsync(output); break;
                case "dev": await app.ServeAsync(); break;
            }
        });

        Assert.Contains("StaticSite.Info", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
        Assert.Throws<InvalidOperationException>(() => app.Info);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.AddPageService<object>();
        app.AddBuildInput("version", "1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_FreezesConfiguration_AndLoadsContentOnlyAfterPathsSettle(bool serve)
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _root;
        var paths = app.Paths;
        app.AddPageService<object>();
        var loads = 0;
        var routeCalls = 0;
        string? observedOutput = null;
        app.UseContentSource<Post>(provider =>
        {
            loads++;
            observedOutput = provider.GetRequiredService<ResolvedSitePaths>().OutputDirectory;
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<object>());
            return [];
        });
        TestArticleContents.MapSite(app);
        app.AddPages<PostPage>(provider =>
        {
            routeCalls++;
            _ = provider.GetRequiredService<ContentDictionary<Post>>().Count;
            return [];
        });

        Assert.Equal(0, loads);
        Assert.Equal(0, routeCalls);
        Assert.False(Directory.Exists(_root));

        var output = Path.Combine(_root, "output");
        if (serve)
        {
            var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
            await using (server)
            {
                using var client = new HttpClient();
                await client.GetStringAsync(web.Urls.First());
                AssertFrozen();
                await web.StopAsync();
            }
        }
        else
        {
            await app.PublishAsync(output);
            AssertFrozen();
            await app.PublishAsync(output);
            Assert.Equal(2, loads);
        }

        Assert.True(routeCalls > 0);
        Assert.Equal(serve ? Path.Combine(_root, ".kiji", "cache", "site") : output, observedOutput);

        void AssertFrozen()
        {
            Assert.Throws<InvalidOperationException>(() => app.AddPageService<object>());
            Assert.Throws<InvalidOperationException>(() => app.UseImageProcessor(
                () => throw new Xunit.Sdk.XunitException("Factory must not run after execution starts.")));
            Assert.Throws<InvalidOperationException>(() => paths.RootDirectory = _root);
            Assert.Throws<InvalidOperationException>(() => paths.ContentDirectory = "other");
            Assert.Throws<InvalidOperationException>(() => paths.StaticDirectory = "other");
            Assert.Throws<InvalidOperationException>(() => app.Info = TestArticleContents.CreateSiteInfo());
            Assert.Throws<InvalidOperationException>(() => app.AddBuildInput("input.json"));
            Assert.Throws<InvalidOperationException>(() => app.AddBuildInput("version", "2"));
            Assert.Throws<InvalidOperationException>(() => app.UseContentSource<object>(static _ => []));
            Assert.Throws<InvalidOperationException>(() => app.UseMarkdownContent<FrontMatter>(
                _ => throw new Xunit.Sdk.XunitException("Configuration callback must not run after execution starts.")));
            Assert.Throws<InvalidOperationException>(() => app.AddStaticPages());
            Assert.Throws<InvalidOperationException>(() => app.AddStaticPages(typeof(PostPage).Assembly));
            Assert.Throws<InvalidOperationException>(() => app.UseDefaultLayout<MainLayout>());
            Assert.Throws<InvalidOperationException>(() => app.UseNotFoundPage<NotFoundPage>());
            Assert.Throws<InvalidOperationException>(() => app.AddPages<PostPage>(static _ => []));
            Assert.Throws<InvalidOperationException>(() => app.AddSitemap());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
