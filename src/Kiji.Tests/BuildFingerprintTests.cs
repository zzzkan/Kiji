using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class BuildFingerprintTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(65537)]
    public void StreamingHashMatchesBytes(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kiji-hash-{Guid.NewGuid():N}");
        try
        {
            var bytes = new byte[length];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(path, bytes);
            Assert.Equal(BuildFingerprint.HashBytes(bytes), BuildFingerprint.HashFile(path));
        }
        finally { File.Delete(path); }
        if (length == 0) { Assert.Equal(BuildFingerprint.Missing, BuildFingerprint.HashFile(path)); }
    }

    [Fact]
    public void ParametersWithEmbeddedSeparators_DoNotShareFingerprint()
    {
        var first = new Dictionary<string, object?> { ["a"] = "x\nb=y", ["b"] = "z" };
        var second = new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y\nb=z" };
        Assert.NotEqual(BuildFingerprint.HashParameters(first), BuildFingerprint.HashParameters(second));
    }

    [Fact]
    public void NullAndEmptyParameters_DoNotShareFingerprint()
    {
        Assert.NotEqual(
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = null }),
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = "" }));
    }
}
