namespace Kiji.Routing;

/// <summary>
/// Represents a planned page for static site generation, including its source,
/// parameters, routing path, and output location.
/// </summary>
/// <param name="SourceIdentifier">
/// An identifier that uniquely represents the source of this page (for example, a template or file path).
/// </param>
/// <param name="Parameters">
/// A collection of parameters used when generating the page.
/// </param>
/// <param name="RoutePath">
/// The route path at which the page will be available in the generated site.
/// </param>
/// <param name="OutputRelativePath">
/// The relative path of the generated file within the output directory.
/// </param>
/// <param name="ExcludeFromSitemap">
/// Indicates whether this page should be excluded from the generated sitemap.
/// </param>
internal sealed record PlannedPage(
    string SourceIdentifier,
    IReadOnlyDictionary<string, string> Parameters,
    string RoutePath,
    string OutputRelativePath,
    bool ExcludeFromSitemap = false);
