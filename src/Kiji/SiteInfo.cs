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

    /// <summary>Combines a site-relative path with the site's <see cref="BaseUrl"/>.</summary>
    /// <param name="path">A site-relative path, optionally starting with a single slash and including a query or fragment.</param>
    /// <returns>The resolved absolute URI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">The path has surrounding whitespace, is an absolute URI or network-path reference,
    /// or contains a backslash, a control character, or a parent-directory segment (including percent-encoded forms).</exception>
    /// <remarks>
    /// A single leading slash is ignored, preserving the deployment base path.
    /// An empty string or <c>/</c> returns <see cref="BaseUrl"/>. Parent-directory segments are rejected even when they would stay within the site.
    /// </remarks>
    public Uri ResolveUrl(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var relativePath = path.StartsWith('/') ? path[1..] : path;
        if (relativePath.Length == 0)
        {
            return BaseUrl;
        }

        var suffixStart = relativePath.AsSpan().IndexOfAny('?', '#');
        var pathPart = suffixStart < 0 ? relativePath.AsSpan() : relativePath.AsSpan(0, suffixStart);
        if (pathPart.IsEmpty || pathPart[0] == '/'
            || char.IsWhiteSpace(pathPart[0]) || char.IsWhiteSpace(pathPart[^1]))
        {
            throw new ArgumentException("A site-relative path with at most one leading slash and no surrounding whitespace is required.", nameof(path));
        }

        // Inspect decoded path segments before Uri can normalize away '..'. Query and
        // fragment values are not path segments and must retain their original meaning.
        var decodedPath = pathPart.Contains('%') ? Uri.UnescapeDataString(pathPart.ToString()).AsSpan() : pathPart;
        foreach (var character in decodedPath)
        {
            if (character == '\\' || char.IsControl(character))
            {
                throw new ArgumentException("The path cannot contain backslashes or control characters.", nameof(path));
            }
        }

        foreach (var segment in decodedPath.Split('/'))
        {
            if (decodedPath[segment].SequenceEqual(".."))
            {
                throw new ArgumentException("The path cannot contain parent-directory segments.", nameof(path));
            }
        }

        if (!Uri.TryCreate(relativePath, UriKind.Relative, out var relativeUri))
        {
            throw new ArgumentException("The path must be site-relative, not an absolute URI.", nameof(path));
        }

        return new Uri(BaseUrl, relativeUri);
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

        return value.AbsoluteUri.EndsWith('/') ? value : new Uri(value.AbsoluteUri + '/');
    }

    private static string ValidateRequiredText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
