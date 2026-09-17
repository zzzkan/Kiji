using Xunit;

namespace Kiji.Tests;

public sealed class SlugTests
{
    [Theory]
    [InlineData("VS Code", "vs-code")]
    [InlineData("Hello, World!", "hello-world")]
    [InlineData("  spaced  out  ", "spaced-out")]
    public void Normalize_ReturnsCanonicalSlug(string tagName, string expected)
    {
        var actual = Slug.Normalize(tagName);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("docs/getting-started")]
    [InlineData("docs\\getting-started")]
    public void Normalize_RejectsMultipleRouteSegments(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Slug.Normalize(value));

        Assert.Contains("single route segment", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    public void Normalize_RejectsEmptyResult(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Slug.Normalize(value));
    }
}
