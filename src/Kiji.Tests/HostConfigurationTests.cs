using Kiji.Assets;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiji.Tests;

public sealed class HostConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-configuration-{Guid.NewGuid():N}");

    public HostConfigurationTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    [Theory]
    [InlineData(null, "base")]
    [InlineData("Staging", "staging")]
    public async Task Configuration_ReadsOnlyTheProjectSettingsForTheEnvironment(string? environmentName, string expected)
    {
        WriteSettings("appsettings.json", """{ "Site": { "Title": "base", "Kept": "yes" } }""");
        WriteSettings("appsettings.Staging.json", """{ "Site": { "Title": "staging" } }""");

        var builder = CreateBuilder(environmentName);

        Assert.Equal(expected, builder.Configuration["Site:Title"]);
        Assert.Equal("yes", builder.Configuration["Site:Kept"]);
        // Machine-specific values would make every publish render every page and break the portable cache.
        Assert.NotNull(Environment.GetEnvironmentVariable("PATH"));
        Assert.Null(builder.Configuration["PATH"]);
        await using var site = builder.Build();
        Assert.Same(builder.Configuration, site.ServiceProvider.GetRequiredService<IConfiguration>());
    }

    [Fact]
    public async Task Publish_RendersPagesAgainWhenConfigurationChanges()
    {
        var output = Path.Combine(_root, "dist");
        async Task<string> PublishAsync(string? overrideTitle = null)
        {
            var builder = CreateBuilder(environmentName: null, typeof(ConfigurationPage));
            if (overrideTitle is not null)
            {
                builder.Configuration.AddInMemoryCollection([new("Site:Title", overrideTitle)]);
            }
            await using var site = builder.Build();
            await site.PublishAsync(output);
            return await File.ReadAllTextAsync(Path.Combine(output, "configuration", "index.html"));
        }

        WriteSettings("appsettings.json", """{ "Site": { "Title": "first" } }""");
        Assert.Contains("title:first", await PublishAsync(), StringComparison.Ordinal);
        WriteSettings("appsettings.json", """{ "Site": { "Title": "second" } }""");
        Assert.Contains("title:second", await PublishAsync(), StringComparison.Ordinal);
        Assert.Contains("title:added", await PublishAsync("added"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImageSettings_BindFromConfigurationWithoutKeepingDefaultWidths()
    {
        WriteSettings("appsettings.json", """{ "Images": { "Widths": [ 480 ], "MaxWidth": 900, "Quality": 70 } }""");
        var builder = CreateBuilder(environmentName: null);
        builder.Services.Configure<ImageProcessorOptions>(builder.Configuration.GetSection("Images"));

        await using var site = builder.Build();
        var options = site.ServiceProvider.GetRequiredService<IOptions<ImageProcessorOptions>>().Value;

        Assert.Equal([480], options.Widths);
        Assert.Equal(900, options.MaxWidth);
        Assert.Equal(70, options.Quality);
    }

    [Fact]
    public async Task Logging_AppliesTheLoggingSectionAndBuilderSettings()
    {
        async Task<bool[]> EnabledLevelsAsync(Action<StaticSiteBuilder>? configure = null)
        {
            var builder = CreateBuilder(environmentName: null);
            configure?.Invoke(builder);
            await using var site = builder.Build();
            var logger = site.ServiceProvider.GetRequiredService<ILogger<HostConfigurationTests>>();
            return [logger.IsEnabled(LogLevel.Debug), logger.IsEnabled(LogLevel.Information), logger.IsEnabled(LogLevel.Warning)];
        }

        var defaults = await EnabledLevelsAsync();
        Assert.Equal([false, false, true], defaults);
        var minimum = await EnabledLevelsAsync(static builder => builder.Logging.SetMinimumLevel(LogLevel.Debug));
        Assert.Equal([true, true, true], minimum);
        WriteSettings("appsettings.json", """{ "Logging": { "LogLevel": { "Default": "Information" } } }""");
        var configured = await EnabledLevelsAsync();
        Assert.Equal([false, true, true], configured);
    }

    private StaticSiteBuilder CreateBuilder(string? environmentName, params Type[] discoveredPages)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root),
            name => name == "DOTNET_ENVIRONMENT" ? environmentName : null, discoveredPages);
        builder.Info = TestArticleContents.CreateSiteInfo();
        return builder;
    }

    private void WriteSettings(string fileName, string json) => File.WriteAllText(Path.Combine(_root, fileName), json);
}
