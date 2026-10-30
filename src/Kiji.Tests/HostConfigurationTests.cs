using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Tests.TestSite;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelativeJson_UsesTheOwnedContentRootProvider(bool publish)
    {
        WriteSettings("live.json", """{ "Value": "loaded" }""");
        var builder = CreateBuilder(environmentName: null);
        builder.Configuration.AddJsonFile("live.json", optional: false, reloadOnChange: true);
        var provider = builder.Environment.ContentRootFileProvider;
        var source = Assert.IsType<JsonConfigurationSource>(builder.Configuration.Sources.Last());

        Assert.Same(provider, source.FileProvider);
        Assert.Equal("loaded", builder.Configuration["Value"]);
        await using var site = builder.Build();
        if (publish)
        {
            await site.PublishAsync(Path.Combine(_root, "dist"));
            Assert.Equal("loaded", builder.Configuration["Value"]);
            Assert.Same(provider, builder.Environment.ContentRootFileProvider);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildFailure_DisposesConfiguration(bool dynamicFactory)
    {
        WriteSettings("live.json", """{ "Value": "loaded" }""");
        var builder = CreateBuilder(environmentName: null);
        builder.Configuration.AddJsonFile("live.json", optional: false, reloadOnChange: true);
        var source = new OnDemandConfigurationSource("Private:Value", "secret");
        builder.Configuration.Add(source);
        if (dynamicFactory)
        {
            var method = new DynamicMethod("Create", typeof(object), [typeof(IServiceProvider)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            builder.Services.AddSingleton(method.CreateDelegate<Func<IServiceProvider, object>>());
            Assert.Throws<NotSupportedException>(() => builder.Build());
        }
        else
        {
            builder.Services.AddSingleton<MissingDependencyService>();
            Assert.Throws<AggregateException>(() => builder.Build());
        }
        Assert.Equal(1, source.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Development_DoesNotAddUserSecretsEvenWhenTheEntryAssemblyDeclaresThem()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SecretsSite"), AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(typeof(UserSecretsIdAttribute).GetConstructor([typeof(string)])!,
            [$"kiji-test-{Guid.NewGuid():N}"]));
        var builder = StaticSite.CreateBuilder([], new SiteExecutionPaths(_root), assembly);
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        await using var site = builder.Build();

        var files = builder.Configuration.Sources.OfType<JsonConfigurationSource>().Select(source => source.Path);
        Assert.Equal(["appsettings.json", "appsettings.Development.json"], files);
    }

    [Fact]
    public async Task Publish_PreservesPreviouslyReadOnDemandKeysInTheSnapshotAndFingerprint()
    {
        var renders = 0;
        async Task PublishAsync(string? value)
        {
            var builder = CreateBuilder(environmentName: null, typeof(ConfigurationSnapshotPage));
            var source = new OnDemandConfigurationSource("Site:Title", value);
            builder.Configuration.Add(source);
            builder.Configuration.Add(new OnDemandConfigurationSource("Captured:Value", "captured"));
            var captured = builder.Configuration.GetSection("Captured:Value");
            var section = builder.Configuration.GetSection("Site");
            Assert.Equal(value, section["Title"]);
            builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Feature"));
            builder.Services.AddSingleton<Action>(() =>
            {
                renders++;
                source.Value = "changed after freezing";
                Assert.Equal(value, section["Title"]);
                Assert.Equal("captured", captured.Value);
                Assert.Contains(builder.Configuration.AsEnumerable(), pair => pair.Key == "Site:Title" && pair.Value == value);
            });
            await using var site = builder.Build();
            await site.PublishAsync(Path.Combine(_root, "dist"));
            Assert.Equal(1, source.DisposeCount);
            var html = await File.ReadAllTextAsync(Path.Combine(_root, "dist", "snapshot", "index.html"));
            Assert.Contains($"title:{value};", html, StringComparison.Ordinal);
        }

        await PublishAsync("first");
        Assert.Equal(1, renders);
        await PublishAsync("first");
        Assert.Equal(1, renders);
        await PublishAsync("second");
        Assert.Equal(2, renders);
        await PublishAsync(null);
        Assert.Equal(3, renders);
    }

    [Theory]
    [InlineData(null, "base")]
    [InlineData("Development", "base")]
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
    public async Task Publish_FreezesConfigurationAndCapturedSectionsBeforeRendering_AndReusesTheSameSnapshot()
    {
        const string first = """{ "Site": { "Title": "first" }, "Feature": { "Enabled": true } }""";
        const string changed = """{ "Site": { "Title": "changed" }, "Feature": { "Enabled": null } }""";
        WriteSettings("live.json", first);
        using var live = new ConfigurationManager();
        live.AddJsonFile(Path.Combine(_root, "live.json"), optional: false, reloadOnChange: false);
        var renders = 0;

        async Task<string> PublishAsync(bool changeDuringRender = false)
        {
            var builder = CreateBuilder(environmentName: null, typeof(ConfigurationSnapshotPage));
            builder.Configuration.AddConfiguration(live);
            var section = builder.Configuration.GetSection("Site");
            builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Feature"));
            builder.Services.AddSingleton<Action>(() =>
            {
                renders++;
                if (changeDuringRender)
                {
                    WriteSettings("live.json", changed);
                    ((IConfigurationRoot)live).Reload();
                    Assert.Equal("first", section["Title"]);
                    Assert.Throws<InvalidOperationException>(() => section["Title"] = "mutated");
                    Assert.Throws<InvalidOperationException>(() => builder.Configuration["Site:Title"] = "mutated");
                    Assert.Throws<NotSupportedException>(() => builder.Configuration.Sources.Clear());
                    Assert.Throws<InvalidOperationException>(() => ((IConfigurationRoot)builder.Configuration).Reload());
                }
            });
            await using var site = builder.Build();
            // Configuration remains editable between Build and execution.
            builder.Configuration.AddInMemoryCollection([new("LateSetting", "included")]);
            await site.PublishAsync(Path.Combine(_root, "dist"));
            return await File.ReadAllTextAsync(Path.Combine(_root, "dist", "snapshot", "index.html"));
        }

        var original = await PublishAsync(changeDuringRender: true);
        Assert.Contains("title:first;enabled:True", original, StringComparison.Ordinal);
        Assert.Equal(1, renders);

        WriteSettings("live.json", first);
        ((IConfigurationRoot)live).Reload();
        Assert.Equal(original, await PublishAsync());
        Assert.Equal(1, renders);

        WriteSettings("live.json", changed);
        ((IConfigurationRoot)live).Reload();
        Assert.Contains("title:changed;enabled:False", await PublishAsync(), StringComparison.Ordinal);
        Assert.Equal(2, renders);
    }

    [Fact]
    public async Task Publish_RendersPagesAgainWhenASettingBecomesNull()
    {
        var output = Path.Combine(_root, "dist");
        async Task<string> PublishAsync()
        {
            var builder = CreateBuilder(environmentName: null, typeof(FeatureOptionsPage));
            builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Feature"));
            await using var site = builder.Build();
            await site.PublishAsync(output);
            return await File.ReadAllTextAsync(Path.Combine(output, "feature", "index.html"));
        }

        WriteSettings("appsettings.json", """{ "Feature": { "Other": "x" } }""");
        Assert.Contains("enabled:True", await PublishAsync(), StringComparison.Ordinal);

        // Every non-null value stays the same, but binding the explicit null changes the option.
        WriteSettings("appsettings.json", """{ "Feature": { "Other": "x", "Enabled": null } }""");
        Assert.Contains("enabled:False", await PublishAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "Site": { "Title": "same" }, "Logging": { "LogLevel": { "Kiji.Build": "Debug" } } }""", true)]
    [InlineData("""{ "Site": { "Title": "same" }, "logging": { "Console": { "FormatterName": "simple" } } }""", true)]
    [InlineData("""{ "Site": { "Title": "same" }, "LoggingTheme": "dark" }""", false)]
    public async Task Fingerprint_LeavesOutOnlyTheLoggingSection(string changed, bool reused)
    {
        async Task<string> PublishAsync()
        {
            var builder = CreateBuilder(environmentName: null, typeof(ConfigurationPage));
            await using var site = builder.Build();
            await site.PublishAsync(Path.Combine(_root, "dist"));
            var manifest = await File.ReadAllTextAsync(Path.Combine(_root, ".kiji", "cache", "manifest.json"));
            return JsonSerializer.Deserialize(manifest, BuildManifestJsonContext.Default.BuildManifest)!.OptionsHash;
        }

        WriteSettings("appsettings.json", """{ "Site": { "Title": "same" } }""");
        var first = await PublishAsync();
        WriteSettings("appsettings.json", changed);

        // Raising a log level to inspect a build must not render every page again.
        Assert.Equal(reused, first == await PublishAsync());
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

    [Theory]
    [InlineData("""{ "Site": { "Name": "Site" } }""", "BaseUrl")]
    [InlineData("""{ "Site": { "BaseUrl": "https://example.com/" } }""", "Name")]
    public void SiteInfo_MissingRequiredValueFromConfiguration_FailsWhenAssigned(string json, string property)
    {
        WriteSettings("appsettings.json", json);
        var builder = CreateBuilder(environmentName: null);
        var info = builder.Configuration.GetRequiredSection("Site").Get<SiteInfo>()!;

        var exception = Assert.Throws<ArgumentException>(() => builder.SiteInfo = info);

        Assert.Contains($"SiteInfo.{property} is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logging_AppliesTheLoggingSection()
    {
        async Task<bool[]> EnabledLevelsAsync()
        {
            var builder = CreateBuilder(environmentName: null);
            await using var site = builder.Build();
            var logger = site.ServiceProvider.GetRequiredService<ILogger<HostConfigurationTests>>();
            return [logger.IsEnabled(LogLevel.Debug), logger.IsEnabled(LogLevel.Information), logger.IsEnabled(LogLevel.Warning)];
        }

        var defaults = await EnabledLevelsAsync();
        Assert.Equal([false, false, true], defaults);
        WriteSettings("appsettings.json", """{ "Logging": { "LogLevel": { "Default": "Information" } } }""");
        var configured = await EnabledLevelsAsync();
        Assert.Equal([false, true, true], configured);
    }

    private StaticSiteBuilder CreateBuilder(string? environmentName, params Type[] discoveredPages)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root),
            name => name == "DOTNET_ENVIRONMENT" ? environmentName : null, discoveredPages);
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        return builder;
    }

    private void WriteSettings(string fileName, string json) => File.WriteAllText(Path.Combine(_root, fileName), json);
}
