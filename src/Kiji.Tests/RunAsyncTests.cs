using System.Diagnostics;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Covers what <see cref="StaticSite.RunAsync(CancellationToken)"/> decides from the
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
    public async Task PublishSite_RejectsSourceAndCacheOutputPathsWithoutChangingSources()
    {
        var source = Path.Combine(_testDir, "contents", "keep.txt");
        await File.WriteAllTextAsync(source, "source data");
        foreach (var output in new[] { _testDir, "contents", "contents/generated", "static", ".kiji", ".kiji/cache/site" })
        {
            await using var app = CreateApp();
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(output));
            Assert.Equal("source data", await File.ReadAllTextAsync(source));
        }
    }

    [Fact]
    public async Task PublishSite_RejectsChangingPathsOnAnExistingApp()
    {
        await using var app = CreateApp();
        await app.PublishAsync(_outputDir);
        var other = Path.Combine(_testDir, "other");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(other));
        Assert.Contains("Create a new app", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(other));
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
    public async Task RunAsync_WithRelativeOutputPath_ResolvesAgainstTheSiteRoot()
    {
        await using var app = CreateApp();

        await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", "out")]), CancellationToken.None);

        var options = app.ServiceProvider.GetRequiredService<ResolvedSitePaths>();
        Assert.Equal(Path.GetFullPath(Path.Combine(_testDir, "out")), options.OutputDirectory);
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

    [Fact]
    public async Task RunAsync_CanceledDevRun_ExitsNormallyWithoutPublishing()
    {
        await using var app = CreateApp();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var exitCode = await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", "   ")]), cts.Token);
        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(_outputDir));
    }

    [Fact]
    public async Task VerbosePublish_EmitsPerPageOutputInIsolatedProcess()
    {
        const string childOutputVariable = "KIJI_TEST_VERBOSE_OUTPUT";
        if (Environment.GetEnvironmentVariable(childOutputVariable) is { } childOutput)
        {
            await using var app = CreateApp();
            await app.RunAsync(EnvironmentWith([("KIJI_OUTPUT", childOutput), ("KIJI_VERBOSE", "true")]), CancellationToken.None);
            return;
        }

        // Run only this test in a child process; BuildOutput and Console are process-wide.
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(RunAsyncTests).Assembly.Location);
        start.ArgumentList.Add("--filter-method");
        start.ArgumentList.Add($"{typeof(RunAsyncTests).FullName}.{nameof(VerbosePublish_EmitsPerPageOutputInIsolatedProcess)}");
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(Path.Combine(_testDir, "results"));
        start.Environment[childOutputVariable] = _outputDir;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        var log = await output;
        Assert.True(process.ExitCode == 0, log + await error);
        Assert.Contains($"Generated: {Path.Combine(_outputDir, "index.html")}", log, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_outputDir, "index.html")));
    }

    private StaticSite CreateApp()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.Paths.RootDirectory = _testDir;
        app.Paths.ContentDirectory = "contents";
        app.Paths.StaticDirectory = "static";

        app.UseContentSource<Post>(static _ => [], static post => post.Slug);
        TestArticleContents.MapSite(app);
        return app;
    }

    private static Func<string, string?> EnvironmentWith(IEnumerable<(string Name, string? Value)> values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }
}
