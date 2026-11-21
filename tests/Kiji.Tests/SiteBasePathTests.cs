using Xunit;

namespace Kiji.Tests;

public sealed class SiteBasePathTests
{
    [Theory]
    [InlineData("https://example.com/", "/")]
    [InlineData("https://example.com/kiji", "/kiji/")]
    [InlineData("https://example.com/kiji/", "/kiji/")]
    [InlineData("https://example.com/a/b", "/a/b/")]
    public void BaseUrl_AbsolutePathIsTheSiteRootWithTrailingSlash(string baseUrl, string expected)
    {
        Assert.Equal(expected, CreateSite(baseUrl).BaseUrl.AbsolutePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("css/app.css")]
    [InlineData("blog/")]
    [InlineData("blog/index.html")]
    public void RelativePath_AcceptsBaseUrlRelativeValues(string value)
    {
        RelativePath.Validate(value, "value");
    }

    [Theory]
    [InlineData("/css/app.css")]
    [InlineData("https://cdn.example.com/x.png")]
    [InlineData("blog/?page=2")]
    [InlineData("blog/#top")]
    [InlineData("blog\\index.html")]
    public void RelativePath_RejectsValuesOutsideItsContract(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => RelativePath.Validate(value, "value"));
        Assert.Equal("value", exception.ParamName);
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
