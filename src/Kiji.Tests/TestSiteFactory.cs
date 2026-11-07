using Kiji.Routing;

namespace Kiji.Tests;

/// <summary>Creates a site builder over the test assembly that discovers only the given pages by default.</summary>
internal static class TestSiteFactory
{
    public static StaticSiteBuilder CreateBuilder(SiteExecutionPaths paths, params Type[] discoveredPages)
        => CreateBuilder(paths, runtimeData: null, discoveredPages: discoveredPages);

    /// <param name="paths">The project and asset paths.</param>
    /// <param name="runtimeData">Reads runtimeconfig properties for the test invocation.</param>
    /// <param name="environment">Reads environment variables for the test invocation.</param>
    /// <param name="discoveredPages">Test pages discovered with their default rendering.</param>
    public static StaticSiteBuilder CreateBuilder(SiteExecutionPaths paths, Func<string, object?>? runtimeData,
        Func<string, string?>? environment = null, params Type[] discoveredPages)
    {
        var builder = StaticSite.CreateBuilder([], paths, typeof(TestSiteFactory).Assembly,
            environment, runtimeData);
        // Empty parameter sets leave out the other test pages.
        var omit = typeof(StaticSiteBuilder).GetMethod(nameof(StaticSiteBuilder.AddPageParameters))!;
        foreach (var type in PageDiscovery.FromAssembly(typeof(TestSiteFactory).Assembly)
            .Select(static page => page.ComponentType).Distinct().Except(discoveredPages))
        {
            omit.MakeGenericMethod(type).Invoke(builder, [new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
        }
        return builder;
    }
}
