using System.Xml.Linq;
using Kiji.Markdown;
using Markdig;
using Xunit;

namespace Kiji.Tests;

public sealed class SecureLinkExtensionTests
{
    [Fact]
    public void MixedDocument_SecuresExternalLinksAndPreservesTheirContent()
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions()
            .Use(new SecureLinkExtension()).Build();
        var markdown = """
            [External text](http://example.com/path?q=1&next=2 "A & B")
            [Secure text](https://example.com/secure)
            <https://example.com/bracket>
            https://example.com/bare
            [Internal](/about)
            [Section](#section)
            [Email](mailto:test@example.com)
            ![Image](https://example.com/image.png)
            """;
        var document = XDocument.Parse("<root>" + global::Markdig.Markdown.ToHtml(markdown, pipeline) + "</root>");
        var links = document.Descendants("a").ToDictionary(link => link.Attribute("href")!.Value);
        foreach (var href in new[] { "http://example.com/path?q=1&next=2", "https://example.com/secure", "https://example.com/bracket", "https://example.com/bare" })
        {
            Assert.Equal("_blank", links[href].Attribute("target")?.Value);
            Assert.Equal("noopener noreferrer", links[href].Attribute("rel")?.Value);
        }
        Assert.Equal("External text", links["http://example.com/path?q=1&next=2"].Value);
        Assert.Equal("A & B", links["http://example.com/path?q=1&next=2"].Attribute("title")?.Value);
        foreach (var href in new[] { "/about", "#section", "mailto:test@example.com" })
        {
            Assert.Null(links[href].Attribute("target"));
            Assert.Null(links[href].Attribute("rel"));
        }
        var image = Assert.Single(document.Descendants("img"));
        Assert.Equal("https://example.com/image.png", image.Attribute("src")?.Value);
        Assert.Null(image.Attribute("target"));
    }
}
