using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Covers what <see cref="KijiApp.RunAsync(CancellationToken)"/> decides from the
/// environment. The dev-server branch is exercised by <see cref="DevServerTests"/>;
/// what matters here is that the presence of <c>KIJI_OUTPUT</c> is the whole switch,
/// and that each branch settles the site's paths on its own directory.
/// </summary>
public sealed class RunAsyncTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _outputDir;

    public RunAsyncTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"kiji-run-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_testDir, "publish");
        Directory.CreateDirectory(Path.Combine(_testDir, "contents"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithOutputPath_GeneratesTheSiteThere()
    {
        await using var app = CreateApp();

        var exitCode = await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outputDir, "index.html")));
    }

    [Fact]
    public async Task RunAsync_WithOutputPath_SettlesOptionsOnThatDirectory()
    {
        await using var app = CreateApp();

        await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]), CancellationToken.None);

        var options = app.Services.GetRequiredService<SsgOptions>();
        Assert.Equal(Path.GetFullPath(_outputDir), options.OutputPath);
    }

    [Fact]
    public async Task RunAsync_WithRelativeOutputPath_ResolvesAgainstTheSiteRoot()
    {
        await using var app = CreateApp();

        await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", "out")]), CancellationToken.None);

        var options = app.Services.GetRequiredService<SsgOptions>();
        Assert.Equal(Path.GetFullPath(Path.Combine(_testDir, "out")), options.OutputPath);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task RunAsync_WithVerbose_TurnsOnPerFileOutput(string value)
    {
        var original = BuildOutput.Verbose;
        try
        {
            await using var app = CreateApp();

            await app.RunAsync(
                EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("KIJI_VERBOSE", value)]),
                CancellationToken.None);

            Assert.True(BuildOutput.Verbose);
        }
        finally
        {
            BuildOutput.Verbose = original;
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(null)]
    public async Task RunAsync_WithoutVerbose_LeavesPerFileOutputOff(string? value)
    {
        var original = BuildOutput.Verbose;
        try
        {
            BuildOutput.Verbose = true;
            await using var app = CreateApp();

            await app.RunAsync(
                EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("KIJI_VERBOSE", value)]),
                CancellationToken.None);

            Assert.False(BuildOutput.Verbose);
        }
        finally
        {
            BuildOutput.Verbose = original;
        }
    }

    [Fact]
    public async Task RunAsync_WithForce_RebuildsEveryPage()
    {
        await using (var first = CreateApp())
        {
            await first.RunAsync(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]), CancellationToken.None);
        }

        var indexPath = Path.Combine(_outputDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stampBefore = File.GetLastWriteTimeUtc(indexPath);

        await using (var forced = CreateApp())
        {
            await forced.RunAsync(
                EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("KIJI_FORCE", "1")]),
                CancellationToken.None);
        }

        Assert.NotEqual(stampBefore, File.GetLastWriteTimeUtc(indexPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAsync_WithoutOutputPath_DoesNotGenerate(string? value)
    {
        await using var app = CreateApp();
        using var cts = new CancellationTokenSource();

        // No output path means the dev server, which runs until cancelled. Cancelling up
        // front is enough to prove nothing was generated, and keeps the test off a socket.
        await cts.CancelAsync();
        var exitCode = await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", value)]), cts.Token);

        // Ctrl+C on the dev server is a normal exit.
        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(_outputDir));
    }

    [Fact]
    public async Task PublishSiteAsync_WithoutOutputPath_Throws()
    {
        await using var app = CreateApp();

        await Assert.ThrowsAsync<ArgumentException>(() => app.PublishSiteAsync(string.Empty));
    }

    [Fact]
    public async Task Services_BeforeAnythingSettlesPaths_Throws()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;

        await using var app = builder.Build();

        // KijiApp exposes no provider publicly for exactly this reason; reaching for
        // SsgOptions before a run has chosen its paths is a bug, not a defaulting case.
        var exception = Assert.Throws<InvalidOperationException>(
            () => app.Services.GetRequiredService<SsgOptions>());
        Assert.Contains("settled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private KijiApp CreateApp()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.Paths.Root = _testDir;
        builder.Paths.Content = "contents";
        builder.Paths.Static = "static";

        builder.AddContentSource<Post>(static _ => [], static post => post.Slug);

        var app = builder.Build();
        TestArticleContents.MapSite(app);
        return app;
    }

    private static Func<string, string?> EnvironmentWith(IEnumerable<(string Name, string? Value)> values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }
}
