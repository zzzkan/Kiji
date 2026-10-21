using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

/// <summary>Creates components, records render policy, and supplies the site's default import map.</summary>
internal sealed class StaticComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, (ObjectFactory Create, bool AlwaysRender)> Factories = new();

    internal static void ClearCache() => Factories.Clear();

    public IComponent CreateInstance(Type componentType)
    {
        if (componentType == typeof(ImportMap))
        {
            // This is the framework component, with a default for its public parameter.
            // Explicit component parameters can still replace the default during rendering.
#pragma warning disable BL0005
            return new ImportMap { ImportMapDefinition = (PageRenderContext.Current?.Assets ?? Assets.AssetResources.Empty).ReadImportMap() };
#pragma warning restore BL0005
        }

        var (create, alwaysRender) = Factories.GetOrAdd(componentType,
            static type => (ActivatorUtilities.CreateFactory(type, Type.EmptyTypes),
                type.IsDefined(typeof(AlwaysRenderAttribute), inherit: true)));
        if (alwaysRender)
        {
            // Layouts and nested/dynamic children share the output page's recorder.
            // Persisting it as uncacheable makes the next publish render this tree again.
            PageRenderContext.Current?.Dependencies?.DisableCache();
        }
        return (IComponent)create(services, null);
    }
}
