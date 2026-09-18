using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class BuildFingerprintTests
{
    [Fact]
    public void SupportedScalars_HaveStableTypeSensitiveHashes()
    {
        object?[] values = [null, "", "42", true, 'x', (sbyte)-1, (byte)1, (short)-1, (ushort)1, -1, 1U, -1L, ulong.MaxValue, DayOfWeek.Friday, Guid.Empty];
        var hashes = values.Select(value => BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = value })).ToArray();
        Assert.All(hashes, hash => Assert.NotNull(hash));
        Assert.Equal(hashes.Length, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" }),
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["b"] = "x", ["a"] = 1 }));
        Assert.NotEqual(
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = DayOfWeek.Friday }),
            BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = DayOfWeek.Saturday }));
    }

    [Fact]
    public void UnsupportedValues_HaveNoFingerprint()
    {
        object[] values = [new object(), new[] { 1 }, new List<string>(), DateTime.UtcNow, DateTimeOffset.UtcNow, DateOnly.MinValue, TimeOnly.MinValue, 1m, 1d, 1f, (Action)(() => { })];
        foreach (var value in values)
        {
            Assert.Null(BuildFingerprint.HashParameters(new Dictionary<string, object?> { ["value"] = value }));
        }
    }

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
