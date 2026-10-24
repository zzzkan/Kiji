using Kiji.Routing;

namespace Kiji.Tests;

/// <summary>Creates a site over the test assembly that discovers only the given pages by default.</summary>
internal static class TestSiteFactory
{
    public static StaticSite Create(SiteExecutionPaths paths, params Type[] discoveredPages)
    {
        var site = StaticSite.Create([], paths, typeof(TestSiteFactory).Assembly);
        // Empty parameter sets leave out the other test pages.
        var omit = typeof(StaticSite).GetMethod(nameof(StaticSite.AddPageParameters))!;
        foreach (var type in PageDiscovery.FromAssembly(typeof(TestSiteFactory).Assembly)
            .Select(static page => page.ComponentType).Distinct().Except(discoveredPages))
        {
            omit.MakeGenericMethod(type).Invoke(site, [new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
        }
        return site;
    }
}
