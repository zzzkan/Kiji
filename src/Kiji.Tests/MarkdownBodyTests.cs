using Kiji.Markdown;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownBodyTests
{
    [Fact]
    public async Task InMemoryContent_RendersThroughComponentWithoutSourceFiles()
    {
        var content = new MarkdownContent<FrontMatter>("nested/post.md", new FrontMatter { Title = "Post" }, "<p>Hello &amp; 日本語</p>");
        var contents = new ContentDictionary<MarkdownContent<FrontMatter>>([new("nested/post.md", content)]);
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var app = builder.Build();
        app.UsePlanningOptions();

        var html = await new ComponentRenderer(app.ServiceProvider).RenderComponentAsync<MarkdownBody<FrontMatter>>(
            new Dictionary<string, object?> { [nameof(MarkdownBody<>.Content)] = contents["NESTED/POST.MD"] });

        Assert.Equal("<p>Hello &amp; 日本語</p>", html);
        Assert.Equal("nested/post.md", content.Path);
        PageRenderContext.SetCurrent(new PageRenderContext
        {
            RoutePath = "/post/",
            OutputRelativeDirectory = "post",
            OutputUrlDirectory = "/post/",
            Dependencies = new Generation.BuildDependencyRecorder(),
        });
        try
        {
            Assert.Equal(html, await contents[content.Path].RenderAsync());
            Assert.False(PageRenderContext.Current!.Dependencies!.Cacheable);
        }
        finally { PageRenderContext.SetCurrent(null); }
    }
}
