using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class OutputPathValidatorTests
{
    [Fact]
    public void ResolveUnderRoot_ResolvesNestedRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "kiji-output");

        var result = OutputPathValidator.ResolveUnderRoot(root, "docs/index.html", "Test output path");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "docs/index.html")), result);
    }

    [Fact]
    public void ResolveUnderRoot_RejectsAbsolutePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "kiji-output");
        var absolute = Path.Combine(root, "index.html");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OutputPathValidator.ResolveUnderRoot(root, absolute, "Test output path"));

        Assert.Contains("must be relative", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../index.html")]
    [InlineData("docs/../../index.html")]
    public void ResolveUnderRoot_RejectsTraversal(string relativePath)
    {
        var root = Path.Combine(Path.GetTempPath(), "kiji-output");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OutputPathValidator.ResolveUnderRoot(root, relativePath, "Test output path"));

        Assert.Contains("escapes the output directory", exception.Message, StringComparison.Ordinal);
    }
}
