namespace Kiji;

/// <summary>
/// Site-wide metadata used for rendering, feeds, and sitemaps.
/// </summary>
public sealed record SiteInfo
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

    /// <summary>
    /// The UTC timestamp captured when this instance was created.
    /// Useful for cache-busting generated asset URLs.
    /// </summary>
    public DateTimeOffset BuildTime { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The path component of <see cref="BaseUrl"/>, always starting and ending with
    /// <c>/</c>. <c>"/"</c> when the site is published at the domain root, and
    /// e.g. <c>"/kiji/"</c> when it is published under a sub-path such as a GitHub
    /// Pages project site.
    /// </summary>
    /// <remarks>
    /// The value is percent-encoded, matching what belongs in an <c>href</c>.
    /// The base path is a deployment location only: it never affects the generated
    /// output layout.
    /// </remarks>
    public string BasePath => BaseUrl.AbsolutePath;

    /// <summary>
    /// Resolves a site-root-relative path to a root-relative URL under
    /// <see cref="BasePath"/>. <c>Path("css/app.css")</c> returns <c>/css/app.css</c>
    /// for a site published at the domain root and <c>/kiji/css/app.css</c> for one
    /// published at <c>https://example.com/kiji/</c>.
    /// </summary>
    /// <param name="path">
    /// A site-root-relative path, with or without a leading <c>/</c>. Absolute URLs,
    /// protocol-relative URLs, and fragment- or query-only values are returned unchanged.
    /// </param>
    /// <returns>A root-relative URL, suitable for an <c>href</c> or <c>src</c> attribute.</returns>
    /// <remarks>
    /// Use this for links you write yourself. Canonical, feed, and sitemap URLs already
    /// carry the base path (they derive from <see cref="BaseUrl"/>), and markdown
    /// page-bundle images are document-relative, so neither needs this method.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is document-relative (starts with <c>./</c> or <c>../</c>).
    /// Document-relative URLs resolve against the containing page and must be left alone.
    /// </exception>
    public string Path(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var basePath = BasePath;
        if (path.Length == 0 || path == "/")
        {
            return basePath;
        }

        if (path[0] is '#' or '?'
            || path.StartsWith("//", StringComparison.Ordinal)
            || HasUriScheme(path))
        {
            return path;
        }

        if (path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith("../", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{path}' is document-relative. SiteInfo.Path resolves site-root-relative paths; "
                + "leave document-relative URLs (such as page-bundle image references) unchanged.",
                nameof(path));
        }

        // Domain-root site with an already-rooted path: nothing to prepend.
        if (basePath.Length == 1 && path[0] == '/')
        {
            return path;
        }

        return string.Concat(basePath, path.AsSpan().TrimStart('/'));
    }

    // RFC 3986 scheme: ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":"
    private static bool HasUriScheme(string path)
    {
        if (!char.IsAsciiLetter(path[0]))
        {
            return false;
        }

        for (var i = 1; i < path.Length; i++)
        {
            var character = path[i];
            if (character == ':')
            {
                return true;
            }

            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return false;
    }

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

        return value.ToTrailingSlashUri();
    }

    private static string ValidateRequiredText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
