using Kiji.Markdown;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.Logging;
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
    public async Task RunAsync_PublishFailure_IsLoggedAndRethrown()
    {
        var logs = new RecordingLoggerProvider();
        var builder = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]));
        builder.Logging.AddProvider(logs);
        var site = builder.Build();
        File.Delete(site.Paths.AssetManifestBasePath + ".staticwebassets.publish.runtime.json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => site.RunAsync(CancellationToken.None));

        Assert.Contains(("Kiji.Build", LogLevel.Error, "Publishing failed."), logs.Entries);
    }

    [Fact]
    public async Task RunAsync_CanceledPublish_IsLoggedBeforeDisposalAndReturnsFailure()
    {
        var logs = new RecordingLoggerProvider();
        var builder = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]));
        builder.Logging.AddProvider(logs);
        var site = builder.Build();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.Equal(1, await site.RunAsync(cancellation.Token));

        Assert.Contains(("Kiji.Build", LogLevel.Warning, "Publishing was canceled; output may be incomplete."), logs.Entries);
        Assert.Throws<ObjectDisposedException>(() => site.ServiceProvider);
    }

    [Theory]
    [InlineData(null, "Staging", null, "Staging")]
    [InlineData("Production", "Staging", null, "Production")]
    [InlineData("Production", "Development", "Staging", "Staging")]
    public async Task Serve_UsesWebHostPrecedenceBeforeLoadingProjectSettings(string? dotnet, string? aspnet, string? argument, string expected)
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, $"appsettings.{expected}.json"), """{ "Selected": "yes" }""");
        string[] args = argument is null ? [] : ["--environment", argument, "--Selected", "no"];
        var builder = StaticSite.CreateBuilder(args,
            new SiteExecutionPaths(_testDir), typeof(StaticSite).Assembly,
            EnvironmentWith([("DOTNET_ENVIRONMENT", dotnet), ("ASPNETCORE_ENVIRONMENT", aspnet)]));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var site = builder.Build();

        Assert.Equal(expected, builder.Environment.EnvironmentName);
        Assert.Equal("yes", builder.Configuration["Selected"]);
        var (server, web) = await site.StartDevServerAsync([.. args, .. TestUrls.EphemeralPort], CancellationToken.None,
            new Kiji.Hosting.DevServerStatusReporter(TextWriter.Null, "kiji", useEmoji: false));
        await using (server)
        {
            Assert.Equal(expected, web.Environment.EnvironmentName);
            Assert.Equal(argument is null ? "yes" : "no", web.Configuration["Selected"]);
        }
    }

    [Theory]
    [InlineData(null, "Production")]
    [InlineData("", "Production")]
    [InlineData("   ", "Production")]
    [InlineData("Staging", "Staging")]
    public async Task Publish_IgnoresWebHostEnvironmentBeforeLoadingProjectSettings(string? dotnet, string expected)
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "appsettings.Development.json"), """{ "Selected": "development" }""");
        await File.WriteAllTextAsync(Path.Combine(_testDir, $"appsettings.{expected}.json"), """{ "Selected": "publish" }""");
        var builder = StaticSite.CreateBuilder(["--environment", "Development"],
            new SiteExecutionPaths(_testDir), typeof(StaticSite).Assembly,
            EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("DOTNET_ENVIRONMENT", dotnet), ("ASPNETCORE_ENVIRONMENT", "Development")]));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var site = builder.Build();

        Assert.Equal(expected, builder.Environment.EnvironmentName);
        Assert.Equal("publish", builder.Configuration["Selected"]);
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
        await CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", "out")])).Build().RunAsync(CancellationToken.None);

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
        await CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])).RunAsync(CancellationToken.None);

        await CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir)])).RunAsync(CancellationToken.None);
        Assert.Equal(1, renders.Counts["first"]);
        Assert.Equal(1, renders.Counts["second"]);

        var indexPath = Path.Combine(_outputDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var stampBefore = File.GetLastWriteTimeUtc(indexPath);

        await CreateMeasuredApp(EnvironmentWith([("KIJI_OUTPUT", _outputDir), ("KIJI_FORCE", "1")])).RunAsync(CancellationToken.None);

        Assert.Equal(2, renders.Counts["first"]);
        Assert.Equal(2, renders.Counts["second"]);
        Assert.Equal(stampBefore, File.GetLastWriteTimeUtc(indexPath));
    }

    [Fact]
    public async Task RunAsync_CanceledDevRun_ExitsNormallyWithoutPublishing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var exitCode = await CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", "   ")])).Build().RunAsync(cts.Token);
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

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("Debug", true, true)]
    [InlineData("Warning", false, false)]
    public async Task Publish_LogsProgressUnderKijiBuild(string? configuredLevel, bool summary, bool details)
    {
        if (configuredLevel is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(_testDir, "appsettings.json"),
                $$"""{ "Logging": { "LogLevel": { "Kiji.Build": "{{configuredLevel}}" } } }""");
        }
        var logs = new RecordingLoggerProvider();
        var builder = CreateBuilder(EnvironmentWith([("KIJI_OUTPUT", _outputDir)]));
        builder.Logging.AddProvider(logs);

        Assert.Equal(0, await builder.Build().RunAsync(CancellationToken.None));

        // Summaries are Information by default, and the Logging section changes the level.
        var build = logs.Entries.Where(static entry => entry.Category == "Kiji.Build").ToArray();
        Assert.Equal(summary, build.Any(static entry => entry.Level == LogLevel.Information
            && entry.Message.StartsWith("Generated ", StringComparison.Ordinal) && entry.Message.Contains(" pages (", StringComparison.Ordinal)));
        Assert.Equal(details, build.Contains(("Kiji.Build", LogLevel.Debug, $"Generated: {Path.Combine(_outputDir, "index.html")}")));
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
