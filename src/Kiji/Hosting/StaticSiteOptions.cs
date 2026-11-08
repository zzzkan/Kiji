using System.Reflection;

namespace Kiji.Hosting;

/// <summary>Site registration values configured before the host is built.</summary>
internal sealed class StaticSiteOptions
{
    internal Type? DefaultLayoutType { get; set; }
    internal Type? NotFoundComponentType { get; set; }
    internal List<Assembly> PageAssemblies { get; } = [];
    internal List<string> ContentPaths { get; } = [];
}
