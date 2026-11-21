using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class BuildFingerprintTests
{
    [Fact]
    public void CodeDependencies_IncludeRootAndTransitiveModuleIdentities()
    {
        var root = typeof(BuildFingerprintTests).Assembly;
        var dependencies = IncrementalBuildPlanner.CollectCodeDependencies([root, root]);
        foreach (var assembly in new[] { root, typeof(StaticSite).Assembly, typeof(object).Assembly })
        {
            Assert.Contains($"{assembly.GetName().Name}:mvid:{assembly.ManifestModule.ModuleVersionId:N}", dependencies);
        }
        Assert.Equal(dependencies.Count, dependencies.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(dependencies.Order(StringComparer.Ordinal), dependencies);
    }

    [Fact]
    public void DynamicCode_HasUnavailableIdentity()
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("DynamicSite_" + Guid.NewGuid().ToString("N")),
            System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        Assert.Contains($"{assembly.GetName().Name}:unavailable", IncrementalBuildPlanner.CollectCodeDependencies([assembly]));
    }

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

    [Fact]
    public void ParametersWithEmbeddedSeparators_DoNotShareFingerprint()
    {
        var first = new Dictionary<string, object?> { ["a"] = "x\nb=y", ["b"] = "z" };
        var second = new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y\nb=z" };
        Assert.NotEqual(BuildFingerprint.HashParameters(first), BuildFingerprint.HashParameters(second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(9000)]
    public void FileComparison_DetectsMissingTruncatedAppendedAndSameLengthCorruption(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), "kiji-compare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var expected = new byte[length];
            new Random(42).NextBytes(expected);
            Assert.False(BuildFingerprint.FileEquals(path, expected));
            File.WriteAllBytes(path, expected);
            Assert.True(BuildFingerprint.FileEquals(path, expected));
            File.WriteAllBytes(path, [.. expected, 1]);
            Assert.False(BuildFingerprint.FileEquals(path, expected));
            if (length == 0) { return; }
            File.WriteAllBytes(path, expected.AsSpan(0, length - 1));
            Assert.False(BuildFingerprint.FileEquals(path, expected));
            var changed = expected.ToArray();
            changed[^1] ^= 1;
            File.WriteAllBytes(path, changed);
            Assert.False(BuildFingerprint.FileEquals(path, expected));
        }
        finally { File.Delete(path); }
    }
}
