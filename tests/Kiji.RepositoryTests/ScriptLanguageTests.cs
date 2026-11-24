using Xunit;

namespace Kiji.RepositoryTests;

public sealed class ScriptLanguageTests
{
    private static readonly string[] ForeignExtensions = [".py", ".sh", ".bash", ".rb", ".bat", ".cmd", ".js", ".mjs", ".cjs", ".ts"];

    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        [".agents/skills/verify-site/scripts/screenshot.mjs"] = "Node's Playwright drives the preinstalled browser; Playwright for .NET would download another.",
        ["src/Kiji.Hosting/livereload.js"] = "The dev server injects it into pages the browser runs.",
    };

    [Fact]
    public void RepositoryScriptsArePowerShellOrCSharp()
    {
        var foreign = RepositoryFiles.List(".")
            .Where(path => ForeignExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsServedToABrowser(path) && !Exceptions.ContainsKey(path))
            .ToList();

        Assert.True(foreign.Count == 0,
            "Write repository scripts in PowerShell (run with dotnet pwsh) or C#. If another language has a real advantage, add the file and its reason to ScriptLanguageTests.Exceptions:"
            + Environment.NewLine + string.Join(Environment.NewLine, foreign));
    }

    [Fact]
    public void EveryExceptionStillExists()
    {
        var files = RepositoryFiles.List(".").ToHashSet(StringComparer.Ordinal);

        Assert.All(Exceptions.Keys, path => Assert.True(files.Contains(path), $"{path} is listed in ScriptLanguageTests.Exceptions but no longer exists. Remove the entry."));
    }

    private static bool IsServedToABrowser(string path) =>
        path.Split('/').Contains("wwwroot", StringComparer.Ordinal)
        || (path.StartsWith("tests/Kiji.PackageTests/Fixture/", StringComparison.Ordinal)
        && Path.GetExtension(path) == ".js");
}
