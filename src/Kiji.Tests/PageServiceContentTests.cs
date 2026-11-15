using Kiji.Markdown;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class PageServiceContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-related-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    public async Task IncrementalRelatedPages_MatchFullBuild_AndSkipUnchanged(string change)
    {
        WritePost("first", "shared");
        WritePost("second", "shared");
        var probe = new RelatedProbe();
        await using var app = CreateApp(probe);
        var output = Path.Combine(_root, "dist");
        await app.PublishAsync(output);
        Assert.Equal(2, probe.Calls);
        Assert.Contains("related:second", File.ReadAllText(Path.Combine(output, "service-related", "first", "index.html")), StringComparison.Ordinal);
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(output, "service-related", "first", "index.html"));
        await app.PublishAsync(output);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(output, "service-related", "first", "index.html")));

        Change(change);
        await app.PublishAsync(output);
        var incremental = ReadHtml(output);
        Assert.True(probe.Calls > 2);
        Assert.Contains(change == "add" ? "related:second,third" : "related:</p>",
            incremental[Path.Combine("service-related", "first", "index.html")], StringComparison.Ordinal);

        // A different output can restore the portable cache while its manifest exists.
        File.Delete(Path.Combine(_root, ".kiji", "cache", "manifest.json"));
        var full = Path.Combine(_root, "full");
        await using var fullApp = CreateApp(new RelatedProbe(), output: "full");
        await fullApp.PublishAsync(full);
        var expected = ReadHtml(full);
        Assert.Equal(expected.Keys.Order(), incremental.Keys.Order());
        foreach (var (key, html) in expected)
        {
            Assert.Equal(html, incremental[key]);
        }
    }

    [Fact]
    public async Task DevServer_FileChangesRefreshScopedDerivedData()
    {
        WritePost("first", "shared");
        WritePost("second", "shared");
        await using var app = CreateApp(new RelatedProbe(), output: null);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
                Assert.Contains("related:second", await client.GetStringAsync("/service-related/first/"), StringComparison.Ordinal);
                foreach (var change in new[] { "edit", "add", "delete" })
                {
                    if (change == "delete")
                    {
                        File.Delete(Path.Combine(_root, "content", "third.md"));
                    }
                    else
                    {
                        Change(change);
                    }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var expected = change == "add" ? "related:third" : "related:</p>";
                    while (!(await client.GetStringAsync("/service-related/first/", timeout.Token)).Contains(expected, StringComparison.Ordinal))
                    {
                        await Task.Delay(50, timeout.Token);
                    }
                }
            }
            finally { await web.StopAsync(); }
        }
    }

    [Fact]
    public async Task DevServer_CodeUpdateReevaluatesScopedData()
    {
        WritePost("first", "shared");
        WritePost("second", "shared");
        var includeTags = true;
        await using var app = CreateApp(new RelatedProbe(), () => includeTags, output: null);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
                Assert.Contains("related:second", await client.GetStringAsync("/service-related/first/"), StringComparison.Ordinal);
                includeTags = false;
                await server.ReloadAfterCodeUpdateAsync();
                Assert.Contains("related:</p>", await client.GetStringAsync("/service-related/first/"), StringComparison.Ordinal);
            }
            finally { await web.StopAsync(); }
        }
    }

    private TestSiteHost CreateApp(RelatedProbe probe, Func<bool>? includeTags = null, string? output = "dist")
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root, Path.Combine(_root, "obj", "site")), outputPath: output);
        builder.UseSiteInfo(new SiteInfo { Name = "Related", BaseUrl = new Uri("https://example.test/") });
        TestSite.TestSiteAssets.Bind(builder.GetSitePaths(), Path.Combine(_root, "wwwroot"));
        builder.AddMarkdownContent<FrontMatter>();
        builder.Services.AddScoped<IReadOnlyList<RelatedTag>>(provider => includeTags?.Invoke() == false ? []
            : RelatedTag.Collect(provider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>(), probe));
        builder.Services.AddScoped<RelatedPosts>();
        builder.AddPageParameters<RelatedPage>(provider => provider.GetRequiredService<ContentCollection<MarkdownContent<FrontMatter>>>()
            .Select(post => new
            {
                Slug = Path.GetFileNameWithoutExtension(post.Path),
                ContentKey = post.Path,
            }));
        return builder.BuildSite();
    }

    private void Change(string change)
    {
        switch (change)
        {
            case "edit": WritePost("second", "different-tag"); break;
            case "add": WritePost("third", "shared"); break;
            case "delete": File.Delete(Path.Combine(_root, "content", "second.md")); break;
        }
    }

    private void WritePost(string key, string tag)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(_root, "content", $"{key}.md"), $"---\ntitle: {key}\ntags: [{tag}]\n---\nBody.\n");
    }

    private static Dictionary<string, string> ReadHtml(string directory) => Directory
        .EnumerateFiles(directory, "*.html", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllText);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
