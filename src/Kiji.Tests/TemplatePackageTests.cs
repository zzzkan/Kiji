using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Checks template metadata, packaging placeholders, and files that protect
/// scaffolded projects from inherited repository settings.
/// </summary>
public sealed class TemplatePackageTests
{
    private static readonly string TemplateDirectory = ResolveTemplateDirectory();

    [Fact]
    public async Task Scaffold_UsesLocalPackageToPublishSite()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kiji-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await RunTemplateAsync(root, "install", TemplateDirectory);
            var output = Path.Combine(root, "site");
            await RunTemplateAsync(root, "kiji", "--name", "ExampleSite", "--output", output,
                "--siteName", "Example Custom Site", "--baseUrl", "https://example.test/blog/");
            Assert.True(File.Exists(Path.Combine(output, "ExampleSite.csproj")));
            var program = await File.ReadAllTextAsync(Path.Combine(output, "Program.cs"));
            Assert.Contains("using ExampleSite;", program, StringComparison.Ordinal);
            Assert.Contains("Example Custom Site", program, StringComparison.Ordinal);
            Assert.Contains("https://example.test/blog/", program, StringComparison.Ordinal);
            Assert.DoesNotContain("SITE_NAME", program, StringComparison.Ordinal);
            Assert.DoesNotContain("SITE_BASE_URL", program, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output, "Pages", "PostPage.razor")));
            Assert.True(File.Exists(Path.Combine(output, "Post.cs")));
            Assert.Contains("UseMarkdownContent<PostFrontMatter, Post>", program, StringComparison.Ordinal);
            Assert.Contains("ContentDictionary<Post>", program, StringComparison.Ordinal);

            var projectPath = Path.Combine(output, "ExampleSite.csproj");
            var project = XDocument.Load(projectPath);
            var packageReference = project.Descendants("PackageReference")
                .Single(static element => string.Equals((string?)element.Attribute("Include"), "Kiji", StringComparison.Ordinal));
            // Consume the package exactly as a user's project would: no ProjectReference
            // or manual targets import. Isolate the cache so an older package cannot pass.
            const string version = "0.1.0-package-test";
            var repository = Path.GetFullPath(Path.Combine(TemplateDirectory, "..", ".."));
            var feed = Path.Combine(root, "feed");
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            await RunDotnetAsync(repository, "pack", Path.Combine(repository, "src", "Kiji", "Kiji.csproj"),
                "-c", configuration, "--no-build", "-o", feed,
                $"-p:Version={version}", "-p:MinVerSkip=true",
                $"-p:NuspecOutputPath={Path.Combine(root, "nuspec")}");
            packageReference.SetAttributeValue("Version", version);
            project.Save(projectPath);

            var nugetConfig = Path.Combine(root, "NuGet.Config");
            new XDocument(new XElement("configuration",
                new XElement("packageSources", new XElement("clear"),
                    new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed)),
                    new XElement("add", new XAttribute("key", "nuget.org"),
                        new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement("packageSourceMapping", new XElement("clear"),
                    new XElement("packageSource", new XAttribute("key", "local"),
                        new XElement("package", new XAttribute("pattern", "Kiji"))),
                    new XElement("packageSource", new XAttribute("key", "nuget.org"),
                        new XElement("package", new XAttribute("pattern", "*"))))))
                .Save(nugetConfig);
            await RunDotnetAsync(output, "restore", projectPath, "--configfile", nugetConfig,
                "--packages", Path.Combine(root, "packages"));
            await RunDotnetAsync(output, "build", projectPath, "-c", "Release", "--no-restore");
            var publish = Path.Combine(output, "dist");
            await RunDotnetAsync(output, "publish", projectPath, "-c", "Release", "--no-restore", "-o", publish);

            Assert.True(File.Exists(Path.Combine(publish, "hello-world", "index.html")));
            var home = await File.ReadAllTextAsync(Path.Combine(publish, "index.html"));
            Assert.Contains("href=\"/blog/hello-world/\"", home, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(publish, "sitemap.xml")));
            Assert.Empty(Directory.EnumerateFiles(publish, "*.dll", SearchOption.AllDirectories));
            Assert.True(Directory.Exists(Path.Combine(output, ".kiji")));
            await RunDotnetAsync(output, "clean", projectPath, "-c", "Release");
            Assert.False(Directory.Exists(Path.Combine(output, ".kiji")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RunTemplateAsync(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["DOTNET_CLI_HOME"] = root;
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.ArgumentList.Add("new");
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        // Keep template installation isolated from the developer's registered templates.
        start.ArgumentList.Add("--debug:custom-hive");
        start.ArgumentList.Add(Path.Combine(root, "hive"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    private static async Task RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    /// <summary>
    /// The pack-time XmlPoke target rewrites this exact attribute. A placeholder that is
    /// syntactically valid but nonexistent means a regression fails loudly at restore
    /// rather than shipping a template pinned to the wrong version.
    /// </summary>
    [Fact]
    public void SiteProject_CarriesTheVersionPlaceholderExactlyOnce()
    {
        var csproj = File.ReadAllText(Path.Combine(TemplateDirectory, "KijiSite.csproj"));

        var occurrences = csproj.Split("Version=\"0.0.0-template\"").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("<PackageReference Include=\"Kiji\"", csproj, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without these, a site scaffolded inside a repository that uses central package
    /// management fails to restore (NU1008), because the Kiji reference carries a version.
    /// </summary>
    [Fact]
    public void Template_ShipsMsBuildInheritanceStoppers()
    {
        Assert.True(File.Exists(Path.Combine(TemplateDirectory, "Directory.Build.props")));

        var packages = File.ReadAllText(Path.Combine(TemplateDirectory, "Directory.Packages.props"));
        Assert.Contains(
            "<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>",
            packages,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Template_IgnoresGeneratedOutput()
    {
        var gitignore = File.ReadAllLines(Path.Combine(TemplateDirectory, ".gitignore"));

        Assert.Contains("dist/", gitignore);
        Assert.Contains(".kiji/", gitignore);
    }

    private static string ResolveTemplateDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kiji.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not locate the repository root from the test assembly.");
        return Path.Combine(directory!.FullName, "templates", "site");
    }
}
