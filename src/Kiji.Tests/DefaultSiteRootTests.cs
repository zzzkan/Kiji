using Xunit;

namespace Kiji.Tests;

/// <summary>
/// The default site root is resolved from where the app runs. A site nested inside a
/// larger repository must resolve to its own directory, not the repository root, or it
/// silently reads <c>contents/</c> from the wrong place.
/// </summary>
public sealed class DefaultSiteRootTests : IDisposable
{
    private readonly string _testDir;

    public DefaultSiteRootTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"DefaultSiteRootTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveDefaultRoot_ForSiteNestedInRepository_IsTheSiteProjectDirectory()
    {
        // repo/.git + repo/site/Site.csproj — the site is one project among many.
        var repoDir = CreateDirectory("repo");
        CreateDirectory("repo", ".git");
        var siteDir = CreateProject("repo", "site");
        var binDir = CreateDirectory("repo", "site", "bin", "Release", "net10.0");

        var root = KijiBuilder.ResolveDefaultRoot(binDir, siteDir);

        Assert.Equal(siteDir, root);
        Assert.NotEqual(repoDir, root);
    }

    [Fact]
    public void ResolveDefaultRoot_ForSiteAtRepositoryRoot_IsTheRepositoryRoot()
    {
        // The common single-site layout: both probes agree, so behavior is unchanged.
        var siteDir = CreateProject("solo");
        CreateDirectory("solo", ".git");
        var binDir = CreateDirectory("solo", "bin", "Release", "net10.0");

        Assert.Equal(siteDir, KijiBuilder.ResolveDefaultRoot(binDir, siteDir));
    }

    [Fact]
    public void ResolveDefaultRoot_WithNoProjectFile_FallsBackToTheRepositoryRoot()
    {
        // A published site has no project file beside it; the .git probe still applies.
        var repoDir = CreateDirectory("published");
        CreateDirectory("published", ".git");
        var runDir = CreateDirectory("published", "app");

        Assert.Equal(repoDir, KijiBuilder.ResolveDefaultRoot(runDir, runDir));
    }

    [Fact]
    public void ResolveDefaultRoot_WithNeitherProjectNorRepository_FallsBackToCurrentDirectory()
    {
        var runDir = CreateDirectory("bare", "app");
        var currentDir = CreateDirectory("bare", "cwd");

        Assert.Equal(currentDir, KijiBuilder.ResolveDefaultRoot(runDir, currentDir));
    }

    [Fact]
    public void ResolveDefaultRoot_PrefersTheRunningAppOverTheCurrentDirectory()
    {
        // dotnet run --project <site> leaves the shell's directory as the current one;
        // the site that is actually running should win.
        var siteDir = CreateProject("app-side");
        var binDir = CreateDirectory("app-side", "bin");
        var elsewhere = CreateProject("elsewhere");

        Assert.Equal(siteDir, KijiBuilder.ResolveDefaultRoot(binDir, elsewhere));
    }

    private string CreateProject(params string[] segments)
    {
        var path = CreateDirectory(segments);
        File.WriteAllText(Path.Combine(path, "Site.csproj"), "<Project />");
        return path;
    }

    private string CreateDirectory(params string[] segments)
    {
        var path = Path.Combine([_testDir, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }
}
