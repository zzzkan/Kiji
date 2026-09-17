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
        Assert.Contains("related:second", File.ReadAllText(Path.Combine(output, "related", "first", "index.html")), StringComparison.Ordinal);
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(output, "related", "first", "index.html"));
        await app.PublishAsync(output);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(output, "related", "first", "index.html")));

        Change(change);
        await app.PublishAsync(output);
        var incremental = ReadHtml(output);
        Assert.True(probe.Calls > 2);
        Assert.Contains(change == "add" ? "related:second,third" : "related:</p>",
            incremental[Path.Combine("related", "first", "index.html")], StringComparison.Ordinal);

        // A different output has no matching manifest and must render from scratch.
        await using var fresh = CreateApp(new RelatedProbe());
        var full = Path.Combine(_root, "full");
        await fresh.PublishAsync(full);
        var expected = ReadHtml(full);
        Assert.Equal(expected.Keys.Order(), incremental.Keys.Order());
        foreach (var (key, html) in expected)
        {
            Assert.Equal(html, incremental[key]);
        }
    }

    [Fact]
    public async Task DevServer_FileChangesRefreshDerivedDictionary()
    {
        WritePost("first", "shared");
        WritePost("second", "shared");
        await using var app = CreateApp(new RelatedProbe());
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
                Assert.Contains("related:second", await client.GetStringAsync("/related/first/"), StringComparison.Ordinal);
                foreach (var change in new[] { "edit", "add", "delete" })
                {
                    if (change == "delete")
                    {
                        File.Delete(Path.Combine(_root, "contents", "third.md"));
                    }
                    else
                    {
                        Change(change);
                    }
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var expected = change == "add" ? "related:third" : "related:</p>";
                    while (!(await client.GetStringAsync("/related/first/", timeout.Token)).Contains(expected, StringComparison.Ordinal))
                    {
                        await Task.Delay(50, timeout.Token);
                    }
                }
            }
            finally { await web.StopAsync(); }
        }
    }

    [Fact]
    public async Task DevServer_CodeUpdateReevaluatesDerivedLoader()
    {
        WritePost("first", "shared");
        WritePost("second", "shared");
        var includeTags = true;
        await using var app = CreateApp(new RelatedProbe(), () => includeTags);
        var (server, web) = await app.StartDevServerAsync(TestUrls.EphemeralPort, CancellationToken.None);
        await using (server)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(web.Urls.First()) };
                Assert.Contains("related:second", await client.GetStringAsync("/related/first/"), StringComparison.Ordinal);
                includeTags = false;
                await server.ReloadAfterCodeUpdateAsync();
                Assert.Contains("related:</p>", await client.GetStringAsync("/related/first/"), StringComparison.Ordinal);
            }
            finally { await web.StopAsync(); }
        }
    }

    private StaticSite CreateApp(RelatedProbe probe, Func<bool>? includeTags = null)
    {
        var app = StaticSite.Create([]);
        app.Info = new SiteInfo { Name = "Related", BaseUrl = new Uri("https://example.test/") };
        app.Paths.RootDirectory = _root;
        app.UseMarkdownContent<FrontMatter>();
        app.UseContentSource<RelatedTag>(provider => includeTags?.Invoke() == false ? []
            : RelatedTag.Collect(provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>(), probe));
        app.AddPageService<RelatedPosts>();
        app.AddPages<RelatedPage>(provider => provider.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>()
            .Select(post => new
            {
                Slug = Path.GetFileNameWithoutExtension(post.Value.FileInfo.Name),
                ContentKey = post.Key,
            }));
        return app;
    }

    private void Change(string change)
    {
        switch (change)
        {
            case "edit": WritePost("second", "different-tag"); break;
            case "add": WritePost("third", "shared"); break;
            case "delete": File.Delete(Path.Combine(_root, "contents", "second.md")); break;
        }
    }

    private void WritePost(string key, string tag)
    {
        Directory.CreateDirectory(Path.Combine(_root, "contents"));
        File.WriteAllText(Path.Combine(_root, "contents", $"{key}.md"), $"---\ntitle: {key}\ntags: [{tag}]\n---\nBody.\n");
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
