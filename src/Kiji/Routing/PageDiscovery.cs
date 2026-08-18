using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace Kiji.Routing;

/// <summary>
/// Resolves Razor components into pages via their <c>@page</c> route templates,
/// either by scanning an assembly or from an explicit type list.
/// </summary>
public static class PageDiscovery
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<DiscoveredPage>> AssemblyCache = new();
    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> ParameterNameCache = new();

    /// <summary>
    /// A component resolved from its <c>@page</c> route template.
    /// </summary>
    public sealed record DiscoveredPage(
        string SourceIdentifier,
        Type ComponentType,
        StaticPageDefinition PageDefinition);

    /// <summary>
    /// Finds the routable pages in an assembly: public, non-abstract
    /// <see cref="IComponent"/> classes declaring at least one <c>@page</c> route
    /// template. Other exported types are ignored. Results are cached per assembly;
    /// the cache is dropped on hot reload.
    /// </summary>
    public static IReadOnlyList<DiscoveredPage> FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return AssemblyCache.GetOrAdd(assembly, static assembly => ScanAssembly(assembly));
    }

    /// <summary>
    /// The <c>[Parameter]</c> property names a component declares. Route mappings may
    /// supply values beyond the route template's own parameters; those must name a real
    /// parameter, or the component would reject them at render time with no indication
    /// of which mapping was at fault.
    /// </summary>
    internal static FrozenSet<string> ParameterNames(Type componentType)
    {
        return ParameterNameCache.GetOrAdd(
            componentType,
            static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.IsDefined(typeof(ParameterAttribute), inherit: true))
                .Select(static property => property.Name)
                .ToFrozenSet(StringComparer.Ordinal));
    }

    internal static void ClearCache()
    {
        AssemblyCache.Clear();
        ParameterNameCache.Clear();
    }

    private static IReadOnlyList<DiscoveredPage> ScanAssembly(Assembly assembly)
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
                pages.Add(CreateDiscoveredPage(attribute.Template, type));
            }
        }

        return EnsureUniqueRoutes(pages);
    }

    /// <summary>
    /// Resolves the given page component types into pages. Every type must be a
    /// non-abstract <see cref="IComponent"/> declaring at least one <c>@page</c> route template.
    /// Duplicate types are tolerated; duplicate route templates are an error.
    /// Compiler-generated types (closures, display classes) are skipped, so a
    /// namespace-filtered <c>assembly.GetTypes()</c> query can be passed directly.
    /// </summary>
    public static IReadOnlyList<DiscoveredPage> FromTypes(IEnumerable<Type> pageTypes)
    {
        ArgumentNullException.ThrowIfNull(pageTypes);

        var pages = new List<DiscoveredPage>();
        var seenTypes = new HashSet<Type>();

        foreach (var pageType in pageTypes)
        {
            if (pageType is null)
            {
                throw new ArgumentException("Page type collection contains a null entry.", nameof(pageTypes));
            }

            if (!seenTypes.Add(pageType))
            {
                continue;
            }

            // Closures and other compiler-generated nested types report the namespace
            // of their declaring type, so namespace-based LINQ queries pick them up.
            if (pageType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                continue;
            }

            if (pageType is not { IsClass: true, IsAbstract: false } || !typeof(IComponent).IsAssignableFrom(pageType))
            {
                throw new InvalidOperationException(
                    $"Type '{pageType.FullName}' is not a routable Razor component. Map only non-abstract classes implementing '{nameof(IComponent)}'.");
            }

            var routeAttributes = pageType.GetCustomAttributes<RouteAttribute>(inherit: false).ToArray();
            if (routeAttributes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Page component '{pageType.FullName}' does not declare a '@page' route template.");
            }

            foreach (var attribute in routeAttributes)
            {
                pages.Add(CreateDiscoveredPage(attribute.Template, pageType));
            }
        }

        return EnsureUniqueRoutes(pages);
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
}
