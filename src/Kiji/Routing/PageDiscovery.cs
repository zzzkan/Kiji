using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Kiji.Routing;

/// <summary>
/// Discovers Razor components annotated with <c>@page</c> route templates.
/// </summary>
public static class PageDiscovery
{
    /// <summary>
    /// A component discovered via its <c>@page</c> route template.
    /// </summary>
    public sealed record DiscoveredPage(
        string SourceIdentifier,
        Type ComponentType,
        StaticPageDefinition PageDefinition);

    /// <summary>
    /// Scans the assembly for components declaring <c>@page</c> route templates.
    /// </summary>
    public static IReadOnlyList<DiscoveredPage> LoadDiscoveredPages(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var pages = assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false } && typeof(IComponent).IsAssignableFrom(type))
            .SelectMany(CreateDiscoveredPages)
            .ToList();

        IReadOnlyList<DiscoveredPage> discoveredPages = [.. pages
            .GroupBy(static page => page.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple components declare the route template '{group.Key}'."))];

        return discoveredPages;
    }

    /// <summary>
    /// Creates a <see cref="DiscoveredPage"/> for the route template declared by the component.
    /// </summary>
    public static DiscoveredPage CreateDiscoveredPage(
        string routeTemplate,
        Type componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeTemplate);
        ArgumentNullException.ThrowIfNull(componentType);

        var normalizedTemplate = StaticPageDefinition.NormalizeRouteTemplate(routeTemplate);
        var pageDefinition = StaticPageDefinition.Create(normalizedTemplate);

        return new DiscoveredPage(
            pageDefinition.SourceIdentifier,
            componentType,
            pageDefinition);
    }

    private static IEnumerable<DiscoveredPage> CreateDiscoveredPages(Type componentType)
    {
        foreach (var attribute in componentType.GetCustomAttributes<RouteAttribute>(inherit: false))
        {
            yield return CreateDiscoveredPage(attribute.Template, componentType);
        }
    }
}
