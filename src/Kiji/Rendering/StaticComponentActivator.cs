using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

/// <summary>Rejects unsupported head components before they silently lose metadata.</summary>
internal sealed class StaticComponentActivator(IServiceProvider services) : IComponentActivator
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> Factories = new();

    public IComponent CreateInstance(Type componentType)
    {
        if (typeof(PageTitle).IsAssignableFrom(componentType)
            || typeof(HeadContent).IsAssignableFrom(componentType)
            || typeof(HeadOutlet).IsAssignableFrom(componentType))
        {
            throw new InvalidOperationException(
                $"{componentType.FullName} is not supported by Kiji. Use Kiji.Components.StaticHeadContent "
                + "to contribute to the generated head. For a title, use <StaticHeadContent><title>...</title></StaticHeadContent>. "
                + "Kiji supplies the head outlet automatically.");
        }

        var factory = Factories.GetOrAdd(componentType,
            static type => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes));
        return (IComponent)factory(services, null);
    }
}
