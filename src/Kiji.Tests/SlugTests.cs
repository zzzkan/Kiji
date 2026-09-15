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
        Assert.Equal(expected, Slug.Create(tagName).Value);
    }

}
