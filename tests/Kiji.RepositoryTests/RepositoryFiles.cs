using System.Diagnostics;

namespace Kiji.RepositoryTests;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<string> List(string pathspec)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", pathspec })
        {
            start.ArgumentList.Add(argument);
        }

        using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var standardError = git.StandardError.ReadToEndAsync();
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        if (git.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files failed: {standardError.Result}");
        }

        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => File.Exists(Path.Combine(Root, path)))
            .Order(StringComparer.Ordinal)];
    }

    public static string ReadText(string path) =>
        File.ReadAllText(Path.Combine(Root, path)).ReplaceLineEndings("\n");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kiji.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kiji.slnx was not found above the test output directory.");
    }
}
