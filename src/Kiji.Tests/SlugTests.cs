using Xunit;

namespace Kiji.Tests;

public sealed class SlugTests
{
    [Theory]
    [InlineData("C#", "c-sharp")]
    [InlineData(".NET", "dot-net")]
    [InlineData("VS Code", "vs-code")]
    [InlineData("Youtube Music", "youtube-music")]
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

    [Fact]
    public void CreateNameMap_Collision_ThrowsInformativeException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Slug.CreateNameMap(["C Sharp", "C#"], "tag", "Tags"));

        Assert.Contains("canonical tag slug 'c-sharp'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Rename one of the tags", exception.Message, StringComparison.Ordinal);
    }
}
