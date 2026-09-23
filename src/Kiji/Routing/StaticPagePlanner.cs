using Kiji.Rendering;

namespace Kiji.Routing;

/// <summary>
/// Provides helper methods to create a plan of static pages from page definitions and site content.
/// </summary>
internal static class StaticPagePlanner
{
    /// <summary>
    /// Plans all static pages by expanding the given page definitions using already discovered dynamic route entries.
    /// </summary>
    /// <param name="pages">The collection of static page definitions to be planned.</param>
    /// <param name="dynamicRoutesByPage">The dynamic route entries keyed by page source identifier.</param>
    /// <returns>A read-only list of planned pages ordered by their output relative path.</returns>
    public static IReadOnlyList<PageRenderRequest> PlanPages(
        IReadOnlyList<PageDiscovery.DiscoveredPage> pages,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> dynamicRoutesByPage)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(dynamicRoutesByPage);

        var plannedPages = new List<PageRenderRequest>();
        foreach (var page in pages.OrderBy(static page => page.SourceIdentifier, StringComparer.OrdinalIgnoreCase))
        {
            if (!page.PageDefinition.IsDynamic)
            {
                plannedPages.Add(CreateStaticPage(page));
                continue;
            }

            plannedPages.AddRange(CreateDynamicPages(page, dynamicRoutesByPage[page.SourceIdentifier]));
        }

        var orderedPages = plannedPages
            .OrderBy(static page => page.OutputRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ValidateUniqueOutputPaths(orderedPages);
        return orderedPages;
    }

    private static PageRenderRequest CreateStaticPage(PageDiscovery.DiscoveredPage page)
    {
        var pathBinding = page.PageDefinition.ResolveStaticPath();

        return new PageRenderRequest(
            page.SourceIdentifier,
            page.ComponentType,
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            pathBinding.RoutePath,
            pathBinding.OutputRelativePath);
    }

    private static List<PageRenderRequest> CreateDynamicPages(
        PageDiscovery.DiscoveredPage page,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> matches)
    {
        return [.. matches.Select(match =>
        {
            var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in page.PageDefinition.ParameterNames)
            {
                var value = Convert.ToString(match[name], System.Globalization.CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidOperationException(
                        $"Route value '{name}' on '{page.ComponentType.FullName}' ('{page.SourceIdentifier}') resolved to null or whitespace.");
                }
                if (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{page.ComponentType.FullName}' supplied invalid route value '{name}' for '{page.SourceIdentifier}': '{value}'. Route values must be a single route segment and cannot contain '/' or '\\'.");
                }
                if (value.Trim() is "." or "..")
                {
                    throw new InvalidOperationException(
                        $"Route mapping for '{page.ComponentType.FullName}' supplied invalid route value '{name}' for '{page.SourceIdentifier}': '{value}'. Route values cannot be '.' or '..'.");
                }
                routeValues.Add(name, value);
            }
            return new PageRenderRequest(
                page.SourceIdentifier,
                page.ComponentType,
                match,
                page.PageDefinition.ResolveRoutePath(routeValues),
                page.PageDefinition.ResolveOutputRelativePath(routeValues));
        })];
    }

    private static void ValidateUniqueOutputPaths(IReadOnlyList<PageRenderRequest> pages)
    {
        var duplicateOutputPath = pages
            .GroupBy(static page => page.OutputRelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateOutputPath is null)
        {
            var outputs = pages.Select(static page => page.OutputRelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var page in pages)
            {
                var directory = Path.GetDirectoryName(page.OutputRelativePath);
                while (!string.IsNullOrEmpty(directory))
                {
                    if (outputs.Contains(directory))
                    {
                        throw new InvalidOperationException(
                            $"Page output '{page.OutputRelativePath}' requires directory '{directory}', which is also a page output file.");
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            }
            return;
        }

        var sources = string.Join(
            ", ",
            duplicateOutputPath.Select(static page => $"'{page.SourceIdentifier}'").Distinct(StringComparer.OrdinalIgnoreCase));

        throw new InvalidOperationException(
            $"Static generation produced duplicate output path '{duplicateOutputPath.Key}' from {sources}. Route selectors must resolve to unique output paths.");
    }
}
