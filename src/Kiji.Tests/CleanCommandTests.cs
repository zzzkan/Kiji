using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Tests for the <c>clean</c> command: it removes the output directory and the
/// <c>.kiji</c> directory and leaves everything else untouched.
/// </summary>
public sealed class CleanCommandTests : IDisposable
{
    private readonly string _testDir;

    public CleanCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"CleanCommandTests_{Guid.NewGuid():N}");
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
    public async Task RunAsync_Clean_DeletesOutputAndKijiDirectoriesOnly()
    {
        var root = Path.Combine(_testDir, "site");
        Directory.CreateDirectory(Path.Combine(root, "dist", "blog"));
        await File.WriteAllTextAsync(Path.Combine(root, "dist", "blog", "index.html"), "<html></html>");
        Directory.CreateDirectory(Path.Combine(root, ".kiji", "cache"));
        await File.WriteAllTextAsync(Path.Combine(root, ".kiji", "cache", "build-manifest.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "contents"));
        await File.WriteAllTextAsync(Path.Combine(root, "contents", "post.md"), "# Post");

        var builder = KijiApp.CreateBuilder(["clean"]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = root;

        await using var app = builder.Build();
        var exitCode = await app.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(Path.Combine(root, "dist")));
        Assert.False(Directory.Exists(Path.Combine(root, ".kiji")));
        Assert.True(File.Exists(Path.Combine(root, "contents", "post.md")));
    }

    [Fact]
    public async Task Clean_MissingDirectories_DoesNotThrow()
    {
        var root = Path.Combine(_testDir, "empty");
        Directory.CreateDirectory(root);

        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = root;

        await using var app = builder.Build();
        app.Clean();

        Assert.False(Directory.Exists(Path.Combine(root, "dist")));
        Assert.False(Directory.Exists(Path.Combine(root, ".kiji")));
    }
}
