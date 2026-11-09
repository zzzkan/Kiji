using System.Net;
using System.Net.Sockets;
using Kiji.Markdown;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kiji.Tests;

/// <summary>Exercises Kiji through the standard host, including process-independent exit codes.</summary>
public sealed class RunAsyncTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), $"kiji-run-{Guid.NewGuid():N}");
    private string OutputDir => Path.Combine(_testDir, "publish");

    public RunAsyncTests()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "content"));
        Directory.CreateDirectory(Path.Combine(_testDir, "static"));
        File.WriteAllText(Path.Combine(_testDir, "static", "keep.css"), "body{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) { Directory.Delete(_testDir, recursive: true); }
    }

    [Fact]
    public async Task Publish_SucceedsThroughStandardRunAsync()
    {
        var host = CreateBuilder(OutputDir).Build();
        var exitCode = (RecordingExitCode)host.Services.GetRequiredService<IProcessExitCode>();
        await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, exitCode.Value);
        Assert.True(File.Exists(Path.Combine(OutputDir, "index.html")));
    }

    [Fact]
    public async Task Publish_MissingAssetCatalog_LogsTheExceptionAndFailsWithoutOutput()
    {
        var logs = new RecordingLoggerProvider();
        var builder = CreateBuilder(OutputDir);
        builder.Logging.AddProvider(logs);
        File.Delete(builder.GetSitePaths().AssetManifestBasePath + ".staticwebassets.publish.runtime.json");
        var host = builder.Build();
        var exitCode = (RecordingExitCode)host.Services.GetRequiredService<IProcessExitCode>();
        await host.RunAsync();
        Assert.Equal(1, exitCode.Value);
        Assert.Contains(logs.Exceptions, static error => error.Message.Contains("Razor SDK and Kiji targets", StringComparison.Ordinal));
        Assert.Contains(("Kiji.Build", LogLevel.Error, "Site publishing failed."), logs.Entries);
        Assert.False(Directory.Exists(OutputDir));
    }

    [Fact]
    public async Task Serve_OccupiedPort_LogsTheFailureAndSetsFailureExitCode()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var logs = new RecordingLoggerProvider();
        var builder = StaticSite.CreateBuilder(["--urls", $"http://127.0.0.1:{port}"],
            new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")), typeof(StaticSite).Assembly);
        builder.UseSiteInfo(TestArticleContents.CreateSiteInfo());
        TestSiteAssets.Bind(builder.GetSitePaths(), Path.Combine(_testDir, "static"));
        builder.Services.AddSingleton<IProcessExitCode, RecordingExitCode>();
        builder.Logging.AddProvider(logs);
        var host = builder.Build();
        var exitCode = (RecordingExitCode)host.Services.GetRequiredService<IProcessExitCode>();

        await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, exitCode.Value);
        Assert.Contains(("Kiji.Build", LogLevel.Error, "Development server failed."), logs.Entries);
    }

    [Fact]
    public async Task Publish_ResolvesRelativeOutputAgainstProjectRoot()
    {
        await CreateBuilder("out").Build().RunAsync();
        Assert.True(File.Exists(Path.Combine(_testDir, "out", "index.html")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_StopApplicationAndExternalCancellation_ReportIncompleteOutput(bool external)
    {
        using var cancellation = new CancellationTokenSource();
        var builder = CreateBuilder(OutputDir);
        builder.AddArtifact("stop.txt", async (_, context, token) =>
        {
            if (external) { cancellation.Cancel(); }
            else { context.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication(); }
            await Task.Delay(Timeout.Infinite, token);
        });
        var host = builder.Build();
        var exitCode = (RecordingExitCode)host.Services.GetRequiredService<IProcessExitCode>();
        await host.RunAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, exitCode.Value);
    }

    [Theory]
    [InlineData(false, null, null, null, "Development")]
    [InlineData(false, null, "Staging", null, "Staging")]
    [InlineData(false, "Production", "Staging", null, "Production")]
    [InlineData(false, "Production", "Development", "Staging", "Staging")]
    [InlineData(true, null, "Development", "Development", "Production")]
    [InlineData(true, "Staging", "Development", "Development", "Staging")]
    public void Environment_SelectsOnlyProjectSettingsForTheExecutionMode(
        bool publish, string? dotnet, string? aspnet, string? argument, string expected)
    {
        File.WriteAllText(Path.Combine(_testDir, $"appsettings.{expected}.json"), """{ "Selected": "yes" }""");
        string[] args = argument is null ? [] : ["--environment", argument, "--Selected", "no"];
        var builder = StaticSite.CreateBuilder(args, new SiteExecutionPaths(_testDir), typeof(StaticSite).Assembly,
            environment: name => name switch { "DOTNET_ENVIRONMENT" => dotnet, "ASPNETCORE_ENVIRONMENT" => aspnet, _ => null },
            runtimeData: name => name == "Kiji.OutputPath" && publish ? OutputDir : null);
        using var host = builder.Build();
        Assert.Equal(expected, builder.Environment.EnvironmentName);
        Assert.Equal(Path.GetFullPath(_testDir), builder.Environment.ContentRootPath);
        Assert.Equal("yes", builder.Configuration["Selected"]);
        Assert.Null(builder.Configuration["PATH"]);
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("root")]
    public void Build_RejectsEnvironmentMutation(string property)
    {
        var builder = CreateBuilder(OutputDir);
        if (property == "environment") { builder.Environment.EnvironmentName = "Changed"; }
        else { builder.Environment.ContentRootPath = Path.GetTempPath(); }
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Do not change", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_RejectsReplacingKijiServiceProviderFactory()
    {
        var builder = CreateBuilder(OutputDir);
        builder.Services.AddOptions<FeatureOptions>()
            .Validate<ContentCollection<Post>>(static (_, content) => content.Count == 0)
            .ValidateOnStart();
        builder.ConfigureContainer(new DefaultServiceProviderFactory());
        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => host.StartAsync());
        Assert.Contains("factory", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(OutputDir));
    }

    [Fact]
    public void Extensions_RejectNonKijiBuilders()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        Assert.Throws<InvalidOperationException>(() => builder.UseSiteInfo(TestArticleContents.CreateSiteInfo()));
        Assert.Throws<InvalidOperationException>(() => builder.AddMarkdownContent<FrontMatter>());
        Assert.Throws<InvalidOperationException>(() => builder.AddPageParameters<Kiji.Tests.TestSite.Pages.PostPage>(static _ => []));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Debug", true)]
    public async Task Publish_PreservesEnvironmentAndProgressLogs(string? configuredLevel, bool details)
    {
        if (configuredLevel is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(_testDir, "appsettings.json"),
                $$"""{ "Logging": { "LogLevel": { "Kiji.Build": "{{configuredLevel}}" } } }""");
        }
        var logs = new RecordingLoggerProvider();
        var builder = CreateBuilder(OutputDir, environment: name => name == "DOTNET_ENVIRONMENT" ? "Staging" : null);
        builder.Logging.AddProvider(logs);
        await builder.Build().RunAsync();
        Assert.Contains(("Kiji.Build", LogLevel.Information, "Publishing for environment 'Staging'."), logs.Entries);
        Assert.Contains(logs.Entries, static entry => entry.Category == "Kiji.Build" && entry.Level == LogLevel.Information && entry.Message.StartsWith("Generated ", StringComparison.Ordinal));
        Assert.Equal(details, logs.Entries.Contains(("Kiji.Build", LogLevel.Debug, $"Generated: {Path.Combine(OutputDir, "index.html")}")));
    }

    [Fact]
    public async Task Publish_RendersAgainWhenEnvironmentChanges()
    {
        async Task<string> PublishAsync(string? environment)
        {
            var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir),
                runtimeData: name => name == "Kiji.OutputPath" ? OutputDir : null,
                environment: name => name == "DOTNET_ENVIRONMENT" ? environment : null,
                discoveredPages: typeof(HostEnvironmentPage));
            builder.UseSiteInfo(TestArticleContents.CreateSiteInfo());
            await builder.Build().RunAsync();
            return await File.ReadAllTextAsync(Path.Combine(OutputDir, "environment", "index.html"));
        }
        Assert.Contains("environment:Production", await PublishAsync(null), StringComparison.Ordinal);
        Assert.Contains("environment:Staging", await PublishAsync("Staging"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupValidation_CanReadContentAndUsesTheRootProviderBeforeOutput()
    {
        var builder = CreateBuilder(OutputDir);
        var loads = 0;
        builder.RegisterContent<object>(provider =>
        {
            loads++;
            Assert.Equal(OutputDir, provider.GetRequiredService<ResolvedSitePaths>().OutputDirectory);
            Assert.False(Directory.Exists(OutputDir));
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ScopedDependency>());
            return [];
        });
        builder.Services.AddScoped<ScopedDependency>();
        builder.Services.AddOptions<FeatureOptions>()
            .Validate<ContentCollection<object>>((_, content) => content.Count == 0)
            .ValidateOnStart();
        var host = builder.Build();
        Assert.Equal(0, loads);
        await host.RunAsync();
        Assert.True(loads > 0);
    }

    private sealed class ScopedDependency;

    private HostApplicationBuilder CreateBuilder(string? outputPath, Func<string, string?>? environment = null)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_testDir, Path.Combine(_testDir, "obj", "site")),
            runtimeData: name => name == "Kiji.OutputPath" ? outputPath : null, environment: environment);
        builder.UseSiteInfo(TestArticleContents.CreateSiteInfo());
        TestSiteAssets.Bind(builder.GetSitePaths(), Path.Combine(_testDir, "static"));
        builder.RegisterContent<Post>(static _ => []);
        TestArticleContents.MapSite(builder);
        return builder;
    }
}
