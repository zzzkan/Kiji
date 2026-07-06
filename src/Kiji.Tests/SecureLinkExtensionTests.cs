using Markdig;
using Xunit;
using Kiji.Markdown;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="SecureLinkExtension"/>.
/// </summary>
public sealed class SecureLinkExtensionTests
{
    private static MarkdownPipeline CreatePipeline()
    {
        return new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Use(new SecureLinkExtension())
            .Build();
    }

    #region External Link Tests

    [Fact]
    public void Process_ExternalHttpLink_AddsSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[External Link](http://example.com)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public void Process_ExternalHttpsLink_AddsSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Secure External Link](https://example.com)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public void Process_ExternalLink_PreservesLinkText()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Click here for more](https://example.com)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains(">Click here for more</a>", html);
    }

    [Fact]
    public void Process_ExternalLink_PreservesTitle()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Link](https://example.com \"Link title\")";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("title=\"Link title\"", html);
    }

    [Fact]
    public void Process_ExternalLink_PreservesUrl()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Link](https://example.com/path/to/page?query=value)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("href=\"https://example.com/path/to/page?query=value\"", html);
    }

    #endregion

    #region Internal Link Tests

    [Fact]
    public void Process_RelativeLink_NoSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Internal Link](/about)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.DoesNotContain("noopener", html);
    }

    [Fact]
    public void Process_AnchorLink_NoSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Section](#section)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.DoesNotContain("noopener", html);
    }

    [Fact]
    public void Process_MailtoLink_NoSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "[Email](mailto:test@example.com)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.DoesNotContain("target=\"_blank\"", html);
        Assert.DoesNotContain("noopener", html);
    }

    #endregion

    #region AutoLink Tests

    [Fact]
    public void Process_AutoLinkBrackets_AddsSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "Visit <https://example.com> for more info.";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public void Process_BareUrl_AddsSecurityAttributes()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "Visit https://example.com for more info.";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("target=\"_blank\"", html);
    }

    #endregion

    #region Image Tests

    [Fact]
    public void Process_ExternalImage_NotAffected()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = "![Image](https://example.com/image.png)";

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        Assert.Contains("<img", html);
        Assert.DoesNotContain("target=\"_blank\"", html); // Images don't get target
    }

    #endregion

    #region Mixed Content Tests

    [Fact]
    public void Process_MixedLinks_CorrectlyIdentifies()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = """
            Here is an [external link](https://example.com) and an [internal link](/about).

            Also a bare URL: https://google.com
            """;

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        // Count occurrences of security attributes
        var targetBlankCount = html.Split("target=\"_blank\"").Length - 1;

        // Should have 2 external links with target="_blank"
        Assert.Equal(2, targetBlankCount);
    }

    [Fact]
    public void Process_MultipleExternalLinks_AllSecured()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var markdown = """
            - [Link 1](https://example1.com)
            - [Link 2](https://example2.com)
            - [Link 3](http://example3.com)
            """;

        // Act
        var html = global::Markdig.Markdown.ToHtml(markdown, pipeline);

        // Assert
        var targetBlankCount = html.Split("target=\"_blank\"").Length - 1;
        Assert.Equal(3, targetBlankCount);
    }

    #endregion
}
