using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

/// <summary>Creates components and rejects unsupported Blazor features.</summary>
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

        var factory = Factories.GetOrAdd(componentType, static type =>
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (typeof(NavigationManager).IsAssignableFrom(property.PropertyType)
                        && property.IsDefined(typeof(InjectAttribute)))
                    {
                        throw new NotSupportedException(
                            $"{type.FullName}.{property.Name} injects NavigationManager, which is not supported by Kiji. "
                            + "Inject PageInfo for the page's public URL and SiteInfo for BaseUrl; use System.Uri for URI conversion. "
                            + "Render links or configure redirects in your hosting platform.");
                    }
                }
            }

            return ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);
        });
        return (IComponent)factory(services, null);
    }
}
