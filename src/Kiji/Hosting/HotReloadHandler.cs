using System.Reflection.Metadata;
using Kiji.Hosting;

[assembly: MetadataUpdateHandler(typeof(HotReloadHandler))]

namespace Kiji.Hosting;

/// <summary>
/// Bridges .NET hot reload (dotnet watch) to the dev server: after code updates
/// are applied to the running process, connected browsers are reloaded so edited
/// Razor components and C# render immediately.
/// </summary>
internal static class HotReloadHandler
{
    /// <summary>
    /// Invoked by the hot reload runtime after metadata updates have been applied.
    /// </summary>
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        _ = updatedTypes;
        DevServer.NotifyCodeUpdated();
    }
}
