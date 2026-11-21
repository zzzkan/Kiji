using System.Reflection.Metadata;
using Kiji.Components;
using Kiji.Hosting;
using Kiji.Routing;

[assembly: MetadataUpdateHandler(typeof(HotReloadHandler))]

namespace Kiji.Hosting;

internal static class HotReloadHandler
{
    internal static void ClearCache(Type[]? updatedTypes)
    {
        _ = updatedTypes;
        PageDiscovery.ClearCache();
        PageView.ClearCache();
    }

    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        _ = updatedTypes;
        DevServer.NotifyCodeUpdated();
    }
}
