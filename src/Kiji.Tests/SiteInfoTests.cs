using Xunit;

namespace Kiji.Tests;

public sealed class SiteInfoTests
{
    private static readonly SiteInfo Site = new() { Name = "Test", BaseUrl = new Uri("https://example.com/kiji/") };

    [Theory]
    [InlineData("about/", "about/")]
    [InlineData("/about/", "about/")]
    [InlineData("/css/app.css", "css/app.css")]
    [InlineData("/日本 語/", "%E6%97%A5%E6%9C%AC%20%E8%AA%9E/")]
    [InlineData("/日本%20語/", "%E6%97%A5%E6%9C%AC%20%E8%AA%9E/")]
    [InlineData("/version..txt", "version..txt")]
    [InlineData("/version%2e%2etxt", "version..txt")]
    [InlineData("/about/?lang=ja#team", "about/?lang=ja#team")]
    [InlineData("/about/?next=../other/#../section", "about/?next=../other/#../section")]
    [InlineData("/about/?next=https://example.org/", "about/?next=https://example.org/")]
    public void ResolveUrl_PreservesBasePathAndUrlSuffix(string path, string expected)
    {
        Assert.Equal("https://example.com/kiji/" + expected, Site.ResolveUrl(path).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/", "")]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com/kiji", "")]
    [InlineData("https://example.com/kiji", "/")]
    public void ResolveUrl_RootPathsReturnBaseUrl(string baseUrl, string path)
    {
        var site = new SiteInfo { Name = "Test", BaseUrl = new Uri(baseUrl) };

        Assert.Equal(site.BaseUrl, site.ResolveUrl(path));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" \t\r\n")]
    [InlineData("/ ")]
    [InlineData("//")]
    [InlineData("///")]
    [InlineData("?query=1")]
    [InlineData("#fragment")]
    [InlineData("/?query=1")]
    [InlineData("/#fragment")]
    [InlineData(" about/")]
    [InlineData("/ about/")]
    [InlineData("about/ ")]
    [InlineData("https://example.org/about/")]
    [InlineData("https://example.com/kiji/about/")]
    [InlineData("/https://example.org/about/")]
    [InlineData("mailto:someone@example.org")]
    [InlineData("file:///tmp/file")]
    [InlineData("//example.org/about/")]
    [InlineData("///example.org/about/")]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("../about/")]
    [InlineData("/../about/")]
    [InlineData("a/../b")]
    [InlineData("a/..")]
    [InlineData("a/..?query=1")]
    [InlineData("a/..#fragment")]
    [InlineData("%2e%2e/about/")]
    [InlineData("a/%2E%2E/b")]
    [InlineData("a/.%2e/b")]
    [InlineData("a/%2e./b")]
    [InlineData("a%2f..%2fb")]
    [InlineData("a/%2e%2e%2fb")]
    [InlineData("\\example.org\\about")]
    [InlineData("/\\example.org/about")]
    [InlineData("a\\..\\b")]
    [InlineData("a%5c..%5cb")]
    [InlineData("a/.\t./b")]
    [InlineData("a/.%0a./b")]
    public void ResolveUrl_RejectsInvalidPaths(string path)
    {
        var exception = Assert.Throws<ArgumentException>(() => Site.ResolveUrl(path));
        Assert.Equal("path", exception.ParamName);
    }

    [Fact]
    public void ResolveUrl_RejectsNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Site.ResolveUrl(null!));
        Assert.Equal("path", exception.ParamName);
    }
}
