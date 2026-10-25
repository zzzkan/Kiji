using System.Diagnostics;
using Kiji.Markdown;
using Kiji.Tests.TestSite;
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
        Directory.CreateDirectory(Path.Combine(_testDir, "content"));
        Directory.CreateDirectory(Path.Combine(_testDir, "static"));
        File.WriteAllText(Path.Combine(_testDir, "static", "keep.css"), "body{}");
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
        var source = Path.Combine(_testDir, "content", "keep.txt");
        await File.WriteAllTextAsync(source, "source data");
        foreach (var output in new[] { _testDir, "content", "content/generated", "static", "static/generated", ".kiji", ".kiji/cache/site" })
        {
            var builder = CreateBuilder();
            builder.AddMarkdownContent<FrontMatter>();
            await using var app = builder.Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(output));
            Assert.Equal("source data", await File.ReadAllTextAsync(source));
        }
    }

    [Fact]
    public async Task PublishSite_RejectsChangingPathsOnAnExistingApp()
    {
        await using var app = CreateBuilder().Build();
        await app.PublishAsync(_outputDir);
        var other = Path.Combine(_testDir, "other");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.PublishAsync(other));
        Assert.Contains("Build a new site", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(other));
    }

    [Fact]
    public async Task RunAsync_MissingAssetCatalog_FailsWithoutPublishingPartialSite()
    {
        await using var app = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])).Build();
        File.Delete(app.Paths.AssetManifestBasePath + ".staticwebassets.publish.runtime.json");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.RunAsync(CancellationToken.None));
        Assert.Contains("Razor SDK and Kiji targets", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_outputDir));
    }

    [Fact]
    public async Task RunAsync_DisposesTheSite_AndRejectsASecondRun()
    {
        var app = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])).Build();

        var exitCode = await app.RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outputDir, "index.html")));
        Assert.Throws<ObjectDisposedException>(() => app.ServiceProvider);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.RunAsync(CancellationToken.None));
        Assert.Contains("only be run once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WithRelativeOutputPath_ResolvesAgainstTheSiteRoot()
    {
        await using var app = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", "out")])).Build();

        await app.RunAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_testDir, "out", "index.html")));
    }

    [Fact]
    public async Task RunAsync_WithForce_RebuildsEveryPage()
    {
        var renders = new AssetRenderLog();
        StaticSite CreateMeasuredApp(Func<string, string?> environment)
        {
            var builder = CreateBuilder(environment);
            builder.AddContentSource<AssetRenderLog>("force-render-log", _ => [new("log", renders, "stable")]);
            builder.AddPageParameters<AssetPage>(_ => [new { Kind = "first" }, new { Kind = "second" }]);
            return builder.Build();
        }
        await using (var first = CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])))
        {
            await first.RunAsync(CancellationToken.None);
        }

        await using (var unchanged = CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])))
        {
            await unchanged.RunAsync(CancellationToken.None);
        }
        Assert.Equal(1, renders.Counts["first"]);
        Assert.Equal(1, renders.Counts["second"]);

        var indexPath = Path.Combine(_outputDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stampBefore = File.GetLastWriteTimeUtc(indexPath);

        await using (var forced = CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("KIJI_FORCE", "1")])))
        {
            await forced.RunAsync(CancellationToken.None);
        }

        Assert.Equal(2, renders.Counts["first"]);
        Assert.Equal(2, renders.Counts["second"]);
        Assert.Equal(stampBefore, File.GetLastWriteTimeUtc(indexPath));
    }

    [Fact]
    public async Task RunAsync_CanceledDevRun_ExitsNormallyWithoutPublishing()
    {
        await using var app = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", "   ")])).Build();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var exitCode = await app.RunAsync(cts.Token);
        Assert.Equal(0, exitCode);
        Assert.False(Directory.Exists(_outputDir));
    }

    [Theory]
    [InlineData(null, null, "Development")]
    [InlineData("dist", null, "Production")]
    [InlineData("dist", "Staging", "Staging")]
    public void Environment_FollowsTheExecutionModeUnlessDotnetEnvironmentIsSet(string? output, string? name, string expected)
    {
        var builder = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", output), ("DOTNET_ENVIRONMENT", name)]));

        Assert.Equal(expected, builder.Environment.EnvironmentName);
        Assert.Equal(Path.GetFullPath(_testDir), builder.Environment.ContentRootPath);
    }

    [Fact]
    public async Task Publish_RendersPagesAgainWhenTheEnvironmentChanges()
    {
        async Task<string> PublishAsync(string? environmentName)
        {
            var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir),
                EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("DOTNET_ENVIRONMENT", environmentName)]),
                typeof(HostEnvironmentPage));
            builder.SiteInfo = TestArticleContents.CreateSiteInfo();
            Assert.Equal(0, await builder.Build().RunAsync(CancellationToken.None));
            return await File.ReadAllTextAsync(Path.Combine(_outputDir, "environment", "index.html"));
        }

        Assert.Contains("environment:Production", await PublishAsync(null), StringComparison.Ordinal);
        Assert.Contains("environment:Staging", await PublishAsync("Staging"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerbosePublish_EmitsPerPageOutputInIsolatedProcess()
    {
        const string childOutputVariable = "KIJI_TEST_VERBOSE_OUTPUT";
        if (Environment.GetEnvironmentVariable(childOutputVariable) is { } childOutput)
        {
            await using var app = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", childOutput), ("KIJI_VERBOSE", "true")])).Build();
            await app.RunAsync(CancellationToken.None);
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

    private StaticSiteBuilder CreateBuilder(Func<string, string?>? environment = null)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")), environment);
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        TestSiteAssets.Bind(builder.Paths, Path.Combine(builder.Paths.ProjectDirectory, "static"));

        builder.AddContentSource<Post>(static _ => []);
        TestArticleContents.MapSite(builder);
        return builder;
    }

    private static Func<string, string?> EnvironmentWith(IEnumerable<(string Name, string? Value)> values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }
}
