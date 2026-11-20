using Xunit;

namespace Kiji.RepositoryTests;

public sealed class CommentScannerTests
{
    [Theory]
    [InlineData("class A { }", 0)]
    [InlineData("/// <summary>Doc.</summary>\nclass A { }", 0)]
    [InlineData("// one\n// block\nclass A { }", 1)]
    [InlineData("// first\n\n// second\nclass A { }", 2)]
    [InlineData("class A { int x; // trailing\n}", 1)]
    [InlineData("/* inline */ class A { }", 1)]
    [InlineData("class A { string s = \"// not a comment\"; }", 0)]
    [InlineData("// see https://example.com\nclass A { }", 1)]
    public void FindsCommentBlocks(string source, int expected) =>
        Assert.Equal(expected, CommentScanner.Blocks(source).Count);
}
