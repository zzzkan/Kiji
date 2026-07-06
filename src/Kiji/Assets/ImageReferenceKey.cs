namespace Kiji.Assets;

/// <summary>
/// Normalizes image references (file paths and markdown URLs) into canonical lookup keys.
/// </summary>
public static class ImageReferenceKey
{
    public static string FromFilePath(string filePath)
    {
        return Normalize(Path.GetFileName(filePath));
    }

    public static string FromMarkdownUrl(string url)
    {
        var path = StripQueryAndFragment(url)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Normalize(Path.GetFileName(path));
    }

    public static string GetStem(string referenceKey)
    {
        var normalizedReferenceKey = referenceKey
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFileNameWithoutExtension(normalizedReferenceKey);
    }

    private static string Normalize(string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : fileName.Replace('\\', '/');
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
