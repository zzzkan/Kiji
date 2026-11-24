using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Kiji.Routing;

internal static class PageDiscovery
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<DiscoveredPage>> AssemblyCache = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, ComponentParameter>> ParameterCache = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DiscoveredPage>> TypeCache = new();

    internal sealed record DiscoveredPage(
        string SourceIdentifier,
        Type ComponentType,
        StaticPageDefinition PageDefinition);

    public static IReadOnlyList<DiscoveredPage> FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return AssemblyCache.GetOrAdd(assembly, static assembly => ScanAssembly(assembly));
    }

    internal static Dictionary<string, ComponentParameter> Parameters(Type componentType)
    {
        return ParameterCache.GetOrAdd(
            componentType,
            static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.IsDefined(typeof(ParameterAttribute), inherit: true))
                .ToDictionary(
                    static property => property.Name,
                    static property => new ComponentParameter(property.PropertyType,
                        property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0),
                    StringComparer.OrdinalIgnoreCase));
    }

    internal static void ClearCache()
    {
        AssemblyCache.Clear();
        ParameterCache.Clear();
        TypeCache.Clear();
    }

    private static List<DiscoveredPage> ScanAssembly(Assembly assembly)
    {
        var pages = new List<DiscoveredPage>();
        foreach (var type in assembly.ExportedTypes)
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(IComponent).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (var attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                pages.Add(CreateDiscoveredPage(attribute.Template, type));
            }
        }

        return pages;
    }

    internal static IReadOnlyList<DiscoveredPage> FromType(Type pageType)
    {
        if (pageType is not { IsClass: true, IsAbstract: false } || !typeof(IComponent).IsAssignableFrom(pageType))
        {
            throw new InvalidOperationException(
                $"Type '{pageType.FullName}' is not a routable Razor component. Only non-abstract classes implementing '{nameof(IComponent)}' can be pages.");
        }

        var pages = TypeCache.GetOrAdd(pageType, static type => EnsureUniqueRoutes(
            [.. type.GetCustomAttributes<RouteAttribute>(inherit: false)
                .Select(attribute => CreateDiscoveredPage(attribute.Template, type))]));
        return pages.Count > 0 ? pages : throw new InvalidOperationException(
            $"Page component '{pageType.FullName}' does not declare a '@page' route template.");
    }

    internal static IReadOnlyList<DiscoveredPage> EnsureUniqueRoutes(IReadOnlyList<DiscoveredPage> pages)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            if (!routes.Add(page.SourceIdentifier))
            {
                throw new InvalidOperationException($"Multiple components declare the route template '{page.SourceIdentifier}'.");
            }
        }
        return pages;
    }

    private static DiscoveredPage CreateDiscoveredPage(
        string routeTemplate,
        Type componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeTemplate);
        ArgumentNullException.ThrowIfNull(componentType);

        var pageDefinition = StaticPageDefinition.Create(routeTemplate);

        return new DiscoveredPage(
            pageDefinition.SourceIdentifier,
            componentType,
            pageDefinition);
    }
}
