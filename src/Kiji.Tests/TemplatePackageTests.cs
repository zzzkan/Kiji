using System.Diagnostics;
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
    public async Task Scaffold_SubstitutesProjectNameAndSiteOptions()
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
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
