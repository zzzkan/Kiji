using Kiji.Generation;
using System.Reflection;
using Kiji.Assets;
using Kiji.Hosting;
using Kiji.Markdown;
using Kiji.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kiji;

/// <summary>Creates generic host builders configured to publish or serve a Kiji site.</summary>
public static class StaticSite
{
    /// <summary>Creates a site host builder with no development server arguments.</summary>
    public static HostApplicationBuilder CreateBuilder() => CreateBuilder([]);

    /// <summary>Creates a site host builder. Arguments configure only the development server.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // Start early so the host and rendering pipeline can use background-compiled methods.
        var runtimeMetadata = SiteRuntimeMetadata.Read(AppContext.GetData);
        PublishJitProfile.Start(runtimeMetadata);
        var entryAssembly = Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("No entry assembly is available. Run the site project as an application.");
        // Read environment variables through configuration providers, not direct process APIs.
        using var hostConfiguration = new ConfigurationManager();
        hostConfiguration.AddEnvironmentVariables();
        return CreateBuilder(args, SiteExecutionPaths.FromRuntimeMetadata(runtimeMetadata, entryAssembly),
            entryAssembly, hostConfiguration, runtimeMetadata);
    }

    internal static HostApplicationBuilder CreateBuilder(string[] args, SiteExecutionPaths paths, Assembly entryAssembly,
        IConfiguration? hostConfiguration = null, SiteRuntimeMetadata? runtimeMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var settings = SiteRunSettings.FromExecutionContext(hostConfiguration ?? new ConfigurationBuilder().Build(), runtimeMetadata ?? new SiteRuntimeMetadata(), args);
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            Args = null,
            ContentRootPath = paths.ProjectDirectory,
            EnvironmentName = settings.EnvironmentName,
            ApplicationName = entryAssembly.GetName().Name,
        });
        // Host settings bootstrap IHostEnvironment but must not fingerprint an absolute
        // checkout path or become application configuration. Keep only project sources.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddJsonFile($"appsettings.{settings.EnvironmentName}.json", optional: true, reloadOnChange: false);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter(BuildLoggerExtensions.CategoryName, LogLevel.Information);
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddConsole(static options => options.LogToStandardErrorThreshold = LogLevel.Warning);
        builder.Metrics.AddConfiguration(builder.Configuration.GetSection("Metrics"));

        ComponentRenderer.AddComponentRenderingServices(builder.Services);

        builder.Services.AddOptions<ImageProcessorOptions>()
            .Validate(static options => options.Quality is >= 1 and <= 100,
                "ImageProcessorOptions.Quality must be between 1 and 100.")
            .Validate(static options => options.MaxWidth > 0,
                "ImageProcessorOptions.MaxWidth must be positive.")
            .Validate(static options => options.Widths is null || options.Widths.All(static width => width > 0),
                "ImageProcessorOptions.Widths must contain only positive widths.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IImageProcessor>(static provider => new ImageProcessor(
            provider.GetRequiredService<IOptions<ImageProcessorOptions>>().Value,
            logger: provider.GetRequiredService<ILoggerFactory>().CreateLogger(BuildLoggerExtensions.CategoryName)));

        builder.Services.AddSingleton(static provider => new ContentPath(
            provider.GetRequiredService<StaticSiteOptions>().ContentPaths
                .Select(provider.GetRequiredService<SiteExecutionPaths>().ResolveAgainstRoot)));
        builder.Services.AddOptions<MarkdownOptions>();
        builder.Services.AddSingleton(static provider => new MarkdownProcessor(
            provider.GetRequiredService<ResolvedSitePaths>(),
            provider.GetRequiredService<SiteExecutionPaths>(),
            provider.GetRequiredService<SiteInfo>(),
            provider.GetRequiredService<ContentPath>(),
            provider.GetRequiredService<IImageProcessor>(),
            provider.GetRequiredService<IOptions<MarkdownOptions>>().Value));

        var options = new StaticSiteOptions();
        builder.Services.AddSingleton(_ => options.SiteInfo ?? throw new InvalidOperationException("Call UseSiteInfo before running the site."));
        options.PageAssemblies.Add(entryAssembly);
        var runtime = new ContentRuntime();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(settings.Publishing ? paths.ResolveForPublish(settings.OutputPath!) : paths.ResolveForDevelopment());
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<SiteEngine>();
        builder.Services.AddSingleton(new SiteServerArguments([.. args]));
        if (settings.Publishing) { builder.Services.AddHostedService<SitePublishService>(); }
        else { builder.Services.AddHostedService<SiteServeService>(); }
        var context = new SiteBuilderContext(builder, options, runtime, paths);
        ((IHostApplicationBuilder)builder).Properties[SiteBuilderContext.Key] = context;
        builder.ConfigureContainer(new KijiServiceProviderFactory(context));
        return builder;
    }
}
