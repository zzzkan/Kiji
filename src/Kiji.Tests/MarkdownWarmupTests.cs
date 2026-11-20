using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownWarmupTests
{
    // The warm-up swallows its own failures, so a sample that stopped parsing, or stopped
    // reaching a default extension, would only show up as a slower publish.
    [Fact]
    public void Sample_RunsFrontMatterAndEveryDefaultExtension()
    {
        var html = MarkdownWarmup.Warm();

        Assert.Contains("<h1 id=\"heading\">", html, StringComparison.Ordinal);
        Assert.Contains("<del>strikethrough</del>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com/\">https://example.com/</a>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"wide\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"footnotes\"", html, StringComparison.Ordinal);
        Assert.Contains("<code class=\"language-csharp\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("title: Title", html, StringComparison.Ordinal);
    }
}
