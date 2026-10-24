using Kiji.Routing;

namespace Kiji.Tests;

/// <summary>Opts unrelated test pages out through empty parameter sets, without changing discovery.</summary>
internal static class TestSiteFactory
{
    public static StaticSite Create(string[] args, SiteExecutionPaths paths, Type? unconfiguredPage = null, bool includeFixedPages = false)
    {
        var site = StaticSite.Create(args, paths);
        var register = typeof(StaticSite).GetMethod(nameof(StaticSite.AddPageParameters))!;
        foreach (var type in PageDiscovery.FromAssembly(typeof(TestSiteFactory).Assembly)
            .Where(page => !includeFixedPages || page.PageDefinition.IsDynamic)
            .Select(static page => page.ComponentType).Distinct())
        {
            if (type != unconfiguredPage)
            {
                register.MakeGenericMethod(type).Invoke(site,
                    [new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
            }
        }
        return site;
    }
}
