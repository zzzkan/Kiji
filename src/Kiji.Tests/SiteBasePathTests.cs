using Xunit;

namespace Kiji.Tests;

public sealed class SiteBasePathTests
{
    [Theory]
    [InlineData("https://example.com", "/")]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com/kiji", "/kiji/")]
    [InlineData("https://example.com/kiji/", "/kiji/")]
    [InlineData("https://example.com/a/b", "/a/b/")]
    public void BasePath_IsThePathComponentWithTrailingSlash(string baseUrl, string expected)
    {
        Assert.Equal(expected, CreateSite(baseUrl).BasePath);
    }

    [Theory]
    [InlineData("css/app.css", "/css/app.css")]
    [InlineData("/css/app.css", "/css/app.css")]
    [InlineData("blog/", "/blog/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    public void Path_WithoutPrefix_ReturnsRootRelativePath(string path, string expected)
    {
        Assert.Equal(expected, CreateSite("https://example.com/").Path(path));
    }

    [Theory]
    [InlineData("css/app.css", "/kiji/css/app.css")]
    [InlineData("/css/app.css", "/kiji/css/app.css")]
    [InlineData("blog/", "/kiji/blog/")]
    [InlineData("blog/?page=2", "/kiji/blog/?page=2")]
    [InlineData("", "/kiji/")]
    [InlineData("/", "/kiji/")]
    public void Path_WithPrefix_PrependsTheBasePath(string path, string expected)
    {
        Assert.Equal(expected, CreateSite("https://example.com/kiji/").Path(path));
    }

    [Theory]
    [InlineData("https://cdn.example.com/x.png")]
    [InlineData("http://cdn.example.com/x.png")]
    [InlineData("//cdn.example.com/x.png")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("data:image/svg+xml,<svg/>")]
    [InlineData("#top")]
    [InlineData("?q=1")]
    public void Path_LeavesNonSiteRelativeValuesUnchanged(string path)
    {
        Assert.Equal(path, CreateSite("https://example.com/kiji/").Path(path));
        Assert.Equal(path, CreateSite("https://example.com/").Path(path));
    }

    // Page-bundle image URLs are document-relative by design; re-rooting them would
    // break them. Rejecting the input keeps that invariant at the API boundary.
    [Theory]
    [InlineData("./cover.webp")]
    [InlineData("../sibling/")]
    public void Path_RejectsDocumentRelativePaths(string path)
    {
        var site = CreateSite("https://example.com/kiji/");

        var exception = Assert.Throws<ArgumentException>(() => site.Path(path));
        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void Path_ThrowsOnNull()
    {
        var site = CreateSite("https://example.com/");

        Assert.Throws<ArgumentNullException>(() => site.Path(null!));
    }

    [Fact]
    public void Path_WithoutPrefixAndRootedPath_ReturnsTheSameInstance()
    {
        const string Path = "/css/app.css";

        Assert.Same(Path, CreateSite("https://example.com/").Path(Path));
    }

    // A hostname that starts like a scheme must not be mistaken for one.
    [Fact]
    public void Path_TreatsSchemeLikeSegmentsWithoutColonAsRelative()
    {
        Assert.Equal("/kiji/https-guide/", CreateSite("https://example.com/kiji/").Path("https-guide/"));
    }

    private static SiteInfo CreateSite(string baseUrl)
    {
        return new SiteInfo
        {
            BaseUrl = new Uri(baseUrl),
            Name = "Test Site",
        };
    }
}
