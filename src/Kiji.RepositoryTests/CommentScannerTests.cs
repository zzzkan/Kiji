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
    [InlineData("// Browsers drop this header:\n// https://example.com/spec\nclass A { }", 0)]
    [InlineData("// external, https://example.com\nclass A { }\n// ours\nclass B { }", 1)]
    public void FindsUnlinkedCommentBlocks(string source, int expected) =>
        Assert.Equal(expected, CommentScanner.UnlinkedBlocks(source).Count());

    [Fact]
    public void ReindentingKeepsABlocksId() =>
        Assert.Equal(
            CommentScanner.UnlinkedBlocks("class A\n{\n    // why\n    // this\n    void M() { }\n}").Single().Id,
            CommentScanner.UnlinkedBlocks("class A\n{\n        // why\n        // this\n        void M() { }\n}").Single().Id);

    [Fact]
    public void RewordingChangesABlocksId() =>
        Assert.NotEqual(
            CommentScanner.UnlinkedBlocks("// keep this\nclass A { }").Single().Id,
            CommentScanner.UnlinkedBlocks("// keep this for now\nclass A { }").Single().Id);
}
