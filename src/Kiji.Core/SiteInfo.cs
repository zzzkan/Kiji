namespace Kiji;

/// <summary>Site metadata for pages, feeds and sitemaps.</summary>
public sealed class SiteInfo
{
    /// <summary>The published site's absolute URL; a trailing slash is added.</summary>
    public required Uri BaseUrl
    {
        get;
        init => field = ValidateBaseUrl(value);
    }

    /// <summary>The site name.</summary>
    public required string Name
    {
        get;
        init => field = ValidateRequiredText(value);
    }

    /// <summary>A short site description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The site language, such as <c>ja</c>. Defaults to <c>en</c>.</summary>
    public string Language { get; init; } = "en";

    /// <summary>The author name.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Resolves a path under <see cref="BaseUrl"/>; a leading slash keeps the base path.</summary>
    /// <param name="path">A path in the site, such as <c>blog/</c> or <c>/about/?tab=2</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">The path could leave the site: it is absolute, contains <c>..</c>, a backslash or a control character, or is only a query or fragment.</exception>
    public Uri ResolveUrl(string path)
    {
        var relativeUri = ParseRelativePath(path, nameof(path));
        return relativeUri is null ? BaseUrl : new Uri(BaseUrl, relativeUri);
    }

    internal static Uri? ParseRelativePath(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);

        var relativePath = path.StartsWith('/') ? path[1..] : path;
        if (relativePath.Length == 0)
        {
            return null;
        }

        var suffixStart = relativePath.AsSpan().IndexOfAny('?', '#');
        var pathPart = suffixStart < 0 ? relativePath.AsSpan() : relativePath.AsSpan(0, suffixStart);
        if (pathPart.IsEmpty)
        {
            throw new ArgumentException("A query or fragment must follow a non-empty site-relative path.", nameof(path));
        }

        if (pathPart[0] == '/'
            || char.IsWhiteSpace(pathPart[0]) || char.IsWhiteSpace(pathPart[^1]))
        {
            throw new ArgumentException("A site-relative path with at most one leading slash and no surrounding whitespace is required.", parameterName);
        }

        var decodedPath = pathPart.Contains('%') ? Uri.UnescapeDataString(pathPart.ToString()).AsSpan() : pathPart;
        foreach (var character in decodedPath)
        {
            if (character == '\\' || char.IsControl(character))
            {
                throw new ArgumentException("The path cannot contain backslashes or control characters.", parameterName);
            }
        }

        foreach (var segment in decodedPath.Split('/'))
        {
            if (decodedPath[segment].SequenceEqual(".."))
            {
                throw new ArgumentException("The path cannot contain parent-directory segments.", parameterName);
            }
        }

        if (!Uri.TryCreate(relativePath, UriKind.Relative, out var relativeUri))
        {
            throw new ArgumentException("The path must be site-relative, not an absolute URI.", parameterName);
        }

        return relativeUri;
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
