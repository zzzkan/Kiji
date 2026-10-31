using Kiji.Markdown;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownBodyTests
{
    [Fact]
    public async Task RendersBodyAsMarkupWithoutWrapper()
    {
        var content = new MarkdownContent<FrontMatter>(new FileInfo(Path.Combine(AppContext.BaseDirectory, "post.md")),
            new FrontMatter(), string.Empty, (_, _) => Task.FromResult("<p>Hello &amp; 日本語</p>"), contentHash: "hash");
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var app = builder.Build();
        app.UsePlanningOptions();

        var html = await new ComponentRenderer(app.ServiceProvider).RenderComponentAsync<MarkdownBody<FrontMatter>>(
            new Dictionary<string, object?> { [nameof(MarkdownBody<>.Content)] = content });

        Assert.Equal("<p>Hello &amp; 日本語</p>", html);
    }
}
