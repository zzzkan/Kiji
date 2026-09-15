namespace Kiji.Routing;

/// <summary>
/// A single expansion of a dynamic route template.
/// </summary>
/// <param name="RouteValues">
/// The page's parameter values. Names matching the route template bind the URL; the
/// rest are passed through to the component.
/// </param>
internal sealed record StaticPageRouteEntry(
    IReadOnlyDictionary<string, string> RouteValues);
