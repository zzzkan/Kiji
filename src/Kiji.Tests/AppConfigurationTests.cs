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

    [Fact]
    public async Task MissingSite_FailsBuildBeforeCreatingOutput_AndCanBeCorrected()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("StaticSiteBuilder.SiteInfo", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_root));
        Assert.Throws<InvalidOperationException>(() => builder.SiteInfo);
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        builder.Services.AddScoped<object>();
        await using var site = builder.Build();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Build_FreezesConfiguration_AndLoadsContentOnlyAfterPathsSettle(bool serve)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        builder.Services.AddScoped<object>();
        var loads = 0;
        var routeCalls = 0;
        string? observedOutput = null;
        string? observedCache = null;
        builder.AddContentSource<Post>(provider =>
        {
            loads++;
            observedOutput = provider.GetRequiredService<ResolvedSitePaths>().OutputDirectory;
            observedCache = provider.GetRequiredService<ResolvedSitePaths>().ImageCacheDirectory;
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<object>());
            return [];
        });
        TestArticleContents.MapSite(builder);
        builder.AddPageParameters<PostPage>(provider =>
        {
            routeCalls++;
            _ = provider.GetRequiredService<ContentDictionary<Post>>().Count;
            return [];
        });

        await using var app = builder.Build();
        AssertFrozen();
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
                await web.StopAsync();
            }
        }
        else
        {
            await app.PublishAsync(output);
            await app.PublishAsync(output);
            Assert.Equal(2, loads);
        }

        Assert.True(routeCalls > 0);
        Assert.Equal(serve ? Path.Combine(_root, ".kiji", "dev-site") : output, observedOutput);
        Assert.Equal(serve ? null
            : Path.Combine(_root, ".kiji", "cache", "images"), observedCache);
        if (serve) { Assert.False(Directory.Exists(Path.Combine(_root, ".kiji", "cache"))); }

        void AssertFrozen()
        {
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.Services.AddScoped<object>());
            Assert.Throws<InvalidOperationException>(() => builder.Environment.EnvironmentName = "Staging");
            Assert.Throws<InvalidOperationException>(() => builder.SiteInfo = TestArticleContents.CreateSiteInfo());
            Assert.Throws<InvalidOperationException>(() => builder.AddContentSource<object>(static _ => []));
            Assert.Throws<InvalidOperationException>(() => builder.AddMarkdownContent<FrontMatter>(
                _ => throw new Xunit.Sdk.XunitException("Configuration callback must not run after the site is built.")));
            Assert.Throws<InvalidOperationException>(() => builder.AddPageAssembly(typeof(PostPage).Assembly));
            Assert.Throws<InvalidOperationException>(() => builder.UseDefaultLayout<MainLayout>());
            Assert.Throws<InvalidOperationException>(() => builder.AddNotFoundPage<NotFoundPage>());
            Assert.Throws<InvalidOperationException>(() => builder.AddPageParameters<PostPage>(static _ => []));
            Assert.Throws<InvalidOperationException>(() => builder.AddSitemap());
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
