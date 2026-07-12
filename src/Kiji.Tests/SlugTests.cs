using Xunit;

namespace Kiji.Tests;

public sealed class SlugTests
{
    [Theory]
    [InlineData("VS Code", "vs-code")]
    [InlineData("Youtube Music", "youtube-music")]
    [InlineData("Hello, World!", "hello-world")]
    [InlineData("  spaced  out  ", "spaced-out")]
    public void Normalize_ReturnsCanonicalSlug(string tagName, string expected)
    {
        var actual = Slug.Normalize(tagName);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Create_ReturnsCanonicalValueObject()
    {
        var actual = Slug.Create(" Hello World ");

        Assert.Equal("hello-world", actual.Value);
    }
}
