using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Kiji.Routing;

/// <summary>
/// Resolves Razor components into pages via their <c>@page</c> route templates,
/// either by scanning an assembly or from an explicit type list.
/// </summary>
internal static class PageDiscovery
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<DiscoveredPage>> AssemblyCache = new();
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, ComponentParameter>> ParameterCache = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<DiscoveredPage>> TypeCache = new();

    /// <summary>
    /// A component resolved from its <c>@page</c> route template.
    /// </summary>
    internal sealed record DiscoveredPage(
        string SourceIdentifier,
        Type ComponentType,
        StaticPageDefinition PageDefinition);

    /// <summary>
    /// Finds parameterless routes on public, non-abstract
    /// <see cref="IComponent"/> classes in an assembly.
    /// Parameterized routes are omitted. Results are cached per assembly;
    /// the cache is dropped on hot reload.
    /// </summary>
    public static IReadOnlyList<DiscoveredPage> FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return AssemblyCache.GetOrAdd(assembly, static assembly => ScanAssembly(assembly));
    }

    /// <summary>
    /// The <c>[Parameter]</c> properties a component declares, including their types and setters. Route mappings may
    /// supply values beyond the route template's own parameters; those must name a real
    /// parameter, or the component would reject them at render time with no indication
    /// of which mapping was at fault.
    /// </summary>
    internal static FrozenDictionary<string, ComponentParameter> Parameters(Type componentType)
    {
        return ParameterCache.GetOrAdd(
            componentType,
            static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.IsDefined(typeof(ParameterAttribute), inherit: true))
                .ToFrozenDictionary(
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
        // Cheapest checks first: metadata flags, then the component hierarchy,
        // then a single attribute read reused for page creation.
        var pages = new List<DiscoveredPage>();
        foreach (var type in assembly.ExportedTypes)
        {
            if (type is not { IsClass: true, IsAbstract: false } || !typeof(IComponent).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (var attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                var page = CreateDiscoveredPage(attribute.Template, type);
                if (!page.PageDefinition.IsDynamic)
                {
                    pages.Add(page);
                }
            }
        }

        // Check registered routes after the not-found override has replaced its source route.
        return pages;
    }

    /// <summary>
    /// Resolves one registered component into pages. The type must be a
    /// non-abstract <see cref="IComponent"/> declaring at least one <c>@page</c> route template.
    /// </summary>
    internal static IReadOnlyList<DiscoveredPage> FromType(Type pageType)
    {
        if (pageType is not { IsClass: true, IsAbstract: false } || !typeof(IComponent).IsAssignableFrom(pageType))
        {
            throw new InvalidOperationException(
                $"Type '{pageType.FullName}' is not a routable Razor component. Map only non-abstract classes implementing '{nameof(IComponent)}'.");
        }

        var pages = TypeCache.GetOrAdd(pageType, static type => EnsureUniqueRoutes(
            [.. type.GetCustomAttributes<RouteAttribute>(inherit: false)
                .Select(attribute => CreateDiscoveredPage(attribute.Template, type))]));
        return pages.Count > 0 ? pages : throw new InvalidOperationException(
            $"Page component '{pageType.FullName}' does not declare a '@page' route template.");
    }

    /// <summary>
    /// Validates that every route template resolves to exactly one component.
    /// </summary>
    internal static IReadOnlyList<DiscoveredPage> EnsureUniqueRoutes(IReadOnlyList<DiscoveredPage> pages)
    {
        return [.. pages
            .GroupBy(static page => page.SourceIdentifier, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple components declare the route template '{group.Key}'."))];
    }

    /// <summary>
    /// Creates a <see cref="DiscoveredPage"/> for the route template declared by the component.
    /// </summary>
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
