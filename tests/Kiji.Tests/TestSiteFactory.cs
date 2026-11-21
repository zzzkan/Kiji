using Kiji.Routing;
using Microsoft.Extensions.Configuration;

namespace Kiji.Tests;

internal static class TestSiteFactory
{
    internal static IConfiguration HostConfiguration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(
            values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value))).Build();

    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, params Type[] discoveredPages)
        => CreateBuilder(paths, runtimeMetadata: new SiteRuntimeMetadata(OutputPath: "dist"), discoveredPages: discoveredPages);

    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, string? outputPath, params Type[] discoveredPages)
        => CreateBuilder(paths, runtimeMetadata: new SiteRuntimeMetadata(OutputPath: outputPath), discoveredPages: discoveredPages);

    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, SiteRuntimeMetadata? runtimeMetadata,
        IConfiguration? hostConfiguration = null, params Type[] discoveredPages)
    {
        Directory.CreateDirectory(paths.ProjectDirectory);
        var builder = StaticSite.CreateBuilder([], paths, typeof(TestSiteFactory).Assembly,
            hostConfiguration, runtimeMetadata);
        var omit = typeof(StaticSiteHostBuilderExtensions).GetMethod(nameof(StaticSiteHostBuilderExtensions.AddPageParameters))!;
        foreach (var type in PageDiscovery.FromAssembly(typeof(TestSiteFactory).Assembly)
            .Select(static page => page.ComponentType).Distinct().Except(discoveredPages))
        {
            omit.MakeGenericMethod(type).Invoke(null, [builder, new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
        }
        return builder;
    }
}
