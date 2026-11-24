using System.Diagnostics;

namespace Kiji.RepositoryTests;

internal sealed class BuildWorkspace(string root) : IDisposable
{
    private static readonly string[] BuildFiles =
    [
        "global.json",
        ".editorconfig",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "src/.editorconfig",
        "src/Directory.Build.props",
        "src/BannedSymbols.txt",
        "tests/Directory.Build.props",
        "tests/BannedSymbols.txt",
    ];

    public string Root { get; } = root;

    public static BuildWorkspace Create()
    {
        var workspace = new BuildWorkspace(Directory.CreateTempSubdirectory("kiji-repository-checks-").FullName);
        foreach (var file in BuildFiles)
        {
            workspace.Write(file, RepositoryFiles.ReadText(file));
        }

        return workspace;
    }

    public BuildWorkspace Library(string name, string items = "")
    {
        Project($"src/{name}/{name}.csproj", items);
        return Project($"tests/{name}.Tests/{name}.Tests.csproj", "");
    }

    public BuildWorkspace Project(string path, string items, string sdk = "Microsoft.NET.Sdk") =>
        Write(path, $"""
            <Project Sdk="{sdk}">
              <ItemGroup>
            {items}
              </ItemGroup>
            </Project>
            """);

    public BuildWorkspace Write(string path, string text)
    {
        var fullPath = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text);
        return this;
    }

    public (int ExitCode, string Output) RunTarget(string project, string target)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "msbuild", project, "-restore", $"-t:{target}", "-p:NoBuild=true", "-nodeReuse:false", "-nologo", "-v:m" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var dotnet = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var standardError = dotnet.StandardError.ReadToEndAsync();
        var output = dotnet.StandardOutput.ReadToEnd();
        dotnet.WaitForExit();
        return (dotnet.ExitCode, output + standardError.Result);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
