using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Kiji.RepositoryTests;

public sealed class SyntheticWorkloadTests
{
    private const string HashFile = "tests/Kiji.RepositoryTests/synthetic-workload.sha256";

    [Fact]
    public void WorkloadIsUnchanged()
    {
        var recorded = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "synthetic-workload.sha256"))
            .Where(line => line.Length > 0)
            .Select(line => line.Split("  ", 2))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        var current = RepositoryFiles.List("benchmarks/Kiji.SyntheticSite/*.cs")
            .Where(path => !path.StartsWith("benchmarks/Kiji.SyntheticSite/Harness/", StringComparison.Ordinal))
            .ToDictionary(path => path, Hash, StringComparer.Ordinal);

        var failures = new List<string>();
        foreach (var (path, hash) in current)
        {
            if (!recorded.TryGetValue(path, out var expected))
            {
                failures.Add($"{path} is new in the frozen benchmark workload. Remove it: results are only comparable while the workload stays the same.");
            }
            else if (hash != expected)
            {
                failures.Add($"{path} is part of the frozen benchmark workload. Revert it: results are only comparable while the workload stays the same. Only a breaking Kiji API change may force an edit that keeps the generated site the same: then record \"{hash}  {path}\" in {HashFile} and say why in the pull request.");
            }
        }

        foreach (var path in recorded.Keys.Where(path => !current.ContainsKey(path)))
        {
            failures.Add($"{path} was removed from the frozen benchmark workload. Restore it.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RepositoryFiles.ReadText(path))));
}
