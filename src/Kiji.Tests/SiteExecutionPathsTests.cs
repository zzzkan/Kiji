using Xunit;

namespace Kiji.Tests;

public sealed class SiteExecutionPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("relative/project")]
    public void MissingOrInvalidMetadata_DoesNotFallBackToWorkingDirectory(object? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SiteExecutionPaths.FromRuntimeMetadata(value, typeof(SiteExecutionPathsTests).Assembly));
        Assert.Contains("Kiji.ProjectDirectory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("rebuild", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedProject_RequiresRebuild()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"KijiMissing_{Guid.NewGuid():N}");
        Assert.Throws<InvalidOperationException>(() => SiteExecutionPaths.FromRuntimeMetadata(missing, typeof(SiteExecutionPathsTests).Assembly));
        Assert.False(Directory.Exists(missing));
    }
}
