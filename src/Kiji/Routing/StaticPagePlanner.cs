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
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> dynamicRoutesByPage)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(dynamicRoutesByPage);

        ValidateDynamicPageCoverage(pages, dynamicRoutesByPage);

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
            new Dictionary<string, object?>(StringComparer.Ordinal),
            pathBinding.RoutePath,
            pathBinding.OutputRelativePath,
            ExcludeFromSitemap: page.PageDefinition.ExcludeFromSitemap);
    }

    private static List<PageRenderRequest> CreateDynamicPages(
        PageDiscovery.DiscoveredPage page,
        IReadOnlyList<IReadOnlyDictionary<string, string>> matches)
    {
        return [.. matches.Select(match => new PageRenderRequest(
                page.SourceIdentifier,
                page.ComponentType,
                match.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.Ordinal),
                page.PageDefinition.ResolveRoutePath(match),
                page.PageDefinition.ResolveOutputRelativePath(match),
                ExcludeFromSitemap: page.PageDefinition.ExcludeFromSitemap))];
    }

    private static void ValidateDynamicPageCoverage(
        IReadOnlyList<PageDiscovery.DiscoveredPage> pages,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> dynamicRoutesByPage)
    {
        var pageSet = pages.Select(static page => page.SourceIdentifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingRoutes = pages
            .Where(static page => page.PageDefinition.IsDynamic)
            .Select(static page => page.SourceIdentifier)
            .Where(sourceIdentifier => !dynamicRoutesByPage.ContainsKey(sourceIdentifier))
            .OrderBy(static sourceIdentifier => sourceIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingRoutes.Length > 0)
        {
            throw new InvalidOperationException(
                $"Dynamic route templates require a static route provider: {string.Join(", ", missingRoutes.Select(route => $"'{route}'"))}.");
        }

        var extraRoutes = dynamicRoutesByPage.Keys
            .Where(route => !pageSet.Contains(route))
            .OrderBy(static route => route, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (extraRoutes.Length > 0)
        {
            throw new InvalidOperationException(
                $"Static route providers returned entries for undeclared routes: {string.Join(", ", extraRoutes.Select(route => $"'{route}'"))}.");
        }
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
