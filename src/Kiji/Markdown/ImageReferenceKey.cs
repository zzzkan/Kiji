namespace Kiji.Markdown;

/// <summary>
/// Normalizes markdown image URLs into canonical lookup keys: the referenced path
/// relative to the markdown file, with forward slashes and no <c>./</c> prefix.
/// </summary>
internal static class ImageReferenceKey
{
    public static string FromMarkdownUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var path = Uri.UnescapeDataString(StripQueryAndFragment(url)).Replace('\\', '/');

        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return path;
    }

    private static string StripQueryAndFragment(string url)
    {
        var queryIndex = url.IndexOf('?');
        var fragmentIndex = url.IndexOf('#');

        return (queryIndex, fragmentIndex) switch
        {
            (-1, -1) => url,
            (-1, var fragment) => url[..fragment],
            (var query, -1) => url[..query],
            (var query, var fragment) => url[..Math.Min(query, fragment)],
        };
    }
}
