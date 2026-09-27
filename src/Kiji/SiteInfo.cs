namespace Kiji;

/// <summary>
/// Site-wide metadata used for rendering, feeds, and sitemaps.
/// </summary>
public sealed class SiteInfo
{
    /// <summary>
    /// The absolute base URL of the published site, normalized to end with a trailing slash.
    /// </summary>
    public required Uri BaseUrl
    {
        get;
        init => field = ValidateBaseUrl(value);
    }

    /// <summary>
    /// The site name, used in titles and feed metadata.
    /// </summary>
    public required string Name
    {
        get;
        init => field = ValidateRequiredText(value);
    }

    /// <summary>
    /// A short description of the site.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The primary language of the site (e.g. <c>ja</c>).
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// The site author name.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Resolves a URI against the site's <see cref="BaseUrl"/>.</summary>
    /// <param name="relativeUri">The URI to resolve. An empty value returns the base URL.</param>
    /// <returns>The resolved absolute URI.</returns>
    /// <remarks>
    /// Uses standard <see cref="Uri"/> resolution: a leading slash replaces the base path,
    /// and an absolute URI replaces the base URL. No site-containment validation is performed.
    /// </remarks>
    public Uri ResolveUrl(string relativeUri) => new(BaseUrl, relativeUri);

    private static Uri ValidateBaseUrl(Uri value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!value.IsAbsoluteUri)
        {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(value));
        }

        if (!string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("BaseUrl must use the HTTP or HTTPS scheme.", nameof(value));
        }

        if (!string.IsNullOrEmpty(value.Query))
        {
            throw new ArgumentException("BaseUrl must not contain a query string.", nameof(value));
        }

        if (!string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException("BaseUrl must not contain a fragment.", nameof(value));
        }

        return value.AbsoluteUri.EndsWith('/') ? value : new Uri(value.AbsoluteUri + '/');
    }

    private static string ValidateRequiredText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
