using System.Reflection;

namespace Kiji.Hosting;

internal sealed class StaticSiteOptions
{
    internal SiteInfo? SiteInfo { get; set; }
    internal Type? DefaultLayoutType { get; set; }
    internal Type? NotFoundComponentType { get; set; }
    internal List<Assembly> PageAssemblies { get; } = [];
    internal List<string> ContentPaths { get; } = [];
}
