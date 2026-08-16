using System.Text.Json;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Cheap assertions over the <c>dotnet new</c> template content. The end-to-end check
/// (scaffold, restore, build, generate) lives in <c>eng/verify-template.ps1</c>, because
/// a scaffolded site references a Kiji version that is not on nuget.org yet. These guard
/// the contracts that script depends on, and the ones whose breakage is silent.
/// </summary>
public sealed class TemplatePackageTests
{
    private static readonly string TemplateDirectory = ResolveTemplateDirectory();

    private static JsonElement TemplateConfig
    {
        get
        {
            var json = File.ReadAllText(Path.Combine(TemplateDirectory, ".template.config", "template.json"));
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            return JsonDocument.Parse(json, options).RootElement.Clone();
        }
    }

    [Fact]
    public void TemplateConfig_DeclaresTheExpectedIdentity()
    {
        var config = TemplateConfig;

        Assert.Equal("kiji", config.GetProperty("shortName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(config.GetProperty("identity").GetString()));
        Assert.Equal("project", config.GetProperty("tags").GetProperty("type").GetString());
        Assert.Equal("C#", config.GetProperty("tags").GetProperty("language").GetString());
    }

    /// <summary>
    /// If sourceName were "Kiji", the template engine would rewrite the
    /// <c>PackageReference Include="Kiji"</c> to the user's project name and every
    /// scaffolded site would fail to restore.
    /// </summary>
    [Fact]
    public void TemplateConfig_SourceNameDoesNotCollideWithThePackageName()
    {
        var sourceName = TemplateConfig.GetProperty("sourceName").GetString();

        Assert.Equal("KijiSite", sourceName);
        Assert.NotEqual("Kiji", sourceName);
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

    /// <summary>
    /// A declared symbol whose token no longer appears anywhere silently stops
    /// substituting, and the scaffolded site keeps the literal placeholder.
    /// </summary>
    [Fact]
    public void TemplateConfig_EverySymbolTokenAppearsInTheTemplateContent()
    {
        var content = Directory
            .EnumerateFiles(TemplateDirectory, "*", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}.template.config{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var symbol in TemplateConfig.GetProperty("symbols").EnumerateObject())
        {
            var token = symbol.Value.GetProperty("replaces").GetString()!;

            Assert.True(
                content.Any(text => text.Contains(token, StringComparison.Ordinal)),
                $"Symbol '{symbol.Name}' replaces '{token}', which appears in no template file.");
        }
    }

    [Fact]
    public void Template_PagesDeclareRoutes()
    {
        var pagesDirectory = Path.Combine(TemplateDirectory, "Pages");
        var pages = Directory.GetFiles(pagesDirectory, "*.razor");

        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            Assert.Contains("@page ", File.ReadAllText(page), StringComparison.Ordinal);
        }
    }

    private static string ResolveTemplateDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kiji.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not locate the repository root from the test assembly.");
        return Path.Combine(directory!.FullName, "src", "Kiji.Templates", "templates", "site");
    }
}
