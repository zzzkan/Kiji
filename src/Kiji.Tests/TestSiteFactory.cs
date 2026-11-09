using Microsoft.Extensions.DependencyInjection;
using Kiji.Routing;

namespace Kiji.Tests;

/// <summary>Creates a site builder over the test assembly that discovers only the given pages by default.</summary>
internal static class TestSiteFactory
{
    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, params Type[] discoveredPages)
        => CreateBuilder(paths, runtimeData: name => name == "Kiji.OutputPath" ? "dist" : null, discoveredPages: discoveredPages);

    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, string? outputPath, params Type[] discoveredPages)
        => CreateBuilder(paths, runtimeData: name => name == "Kiji.OutputPath" ? outputPath : null, discoveredPages: discoveredPages);

    /// <param name="paths">The project and asset paths.</param>
    /// <param name="runtimeData">Reads runtimeconfig properties for the test invocation.</param>
    /// <param name="environment">Reads environment variables for the test invocation.</param>
    /// <param name="discoveredPages">Test pages discovered with their default rendering.</param>
    public static HostApplicationBuilder CreateBuilder(SiteExecutionPaths paths, Func<string, object?>? runtimeData,
        Func<string, string?>? environment = null, params Type[] discoveredPages)
    {
        Directory.CreateDirectory(paths.ProjectDirectory);
        var builder = StaticSite.CreateBuilder([], paths, typeof(TestSiteFactory).Assembly,
            environment, runtimeData);
        // Empty parameter sets leave out the other test pages.
        var omit = typeof(StaticSiteHostBuilderExtensions).GetMethod(nameof(StaticSiteHostBuilderExtensions.AddPageParameters))!;
        foreach (var type in PageDiscovery.FromAssembly(typeof(TestSiteFactory).Assembly)
            .Select(static page => page.ComponentType).Distinct().Except(discoveredPages))
        {
            omit.MakeGenericMethod(type).Invoke(null, [builder, new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
        }
        builder.Services.AddSingleton<IProcessExitCode, RecordingExitCode>();
        return builder;
    }
}
