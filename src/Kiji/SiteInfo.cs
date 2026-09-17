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

    /// <summary>The percent-encoded path of <see cref="BaseUrl"/>, starting and ending with <c>/</c>.</summary>
    public string BasePath => BaseUrl.AbsolutePath;

    /// <summary>Resolves a site-root-relative path under <see cref="BasePath"/>.</summary>
    /// <param name="path">A path with or without a leading slash; absolute URLs, protocol-relative URLs, fragments, and query-only values pass through unchanged.</param>
    /// <exception cref="ArgumentException">The path starts with <c>./</c> or <c>../</c> and must remain document-relative.</exception>
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
