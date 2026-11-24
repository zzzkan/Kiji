using System.Text.RegularExpressions;
using Xunit;

namespace Kiji.RepositoryTests;

public sealed partial class RepositoryCheckCoverageTests
{
    [Fact]
    public void EveryRepositoryCheckHasATestThatTriggersIt()
    {
        var checks = RepositoryFiles.List("*.targets").Concat(RepositoryFiles.List("*.csproj"))
            .SelectMany(path => MSBuildCode().Matches(RepositoryFiles.ReadText(path)).Select(match => (Code: match.Groups[1].Value, Path: path)))
            .Concat(RepositoryFiles.List("tools/Kiji.Analyzers/*.cs")
                .SelectMany(path => AnalyzerId().Matches(RepositoryFiles.ReadText(path)).Select(match => (Code: match.Groups[1].Value, Path: path))))
            .ToList();
        var tests = RepositoryFiles.List("tests/Kiji.Analyzers.Tests/*.cs").Concat(RepositoryFiles.List("tests/Kiji.RepositoryTests/*.cs"))
            .Where(path => !path.EndsWith($"/{nameof(RepositoryCheckCoverageTests)}.cs", StringComparison.Ordinal))
            .Select(RepositoryFiles.ReadText)
            .ToList();

        Assert.NotEmpty(checks);
        var untested = checks.Where(check => !tests.Any(test => test.Contains($"\"{check.Code}\"", StringComparison.Ordinal)
                || test.Contains($"\"error {check.Code}", StringComparison.Ordinal)
                || test.Contains($", \"{check.Code}\",", StringComparison.Ordinal)))
            .Select(check => $"{check.Code} ({check.Path}) has no test that asserts it. Add one in Kiji.Analyzers.Tests or Kiji.RepositoryTests that makes the check fail.")
            .Distinct()
            .ToList();

        Assert.True(untested.Count == 0, string.Join(Environment.NewLine, untested));
    }

    [GeneratedRegex("Code=\"(KR\\d{4})\"", RegexOptions.CultureInvariant)]
    private static partial Regex MSBuildCode();

    [GeneratedRegex("id: \"(KR\\d{4})\"", RegexOptions.CultureInvariant)]
    private static partial Regex AnalyzerId();
}
