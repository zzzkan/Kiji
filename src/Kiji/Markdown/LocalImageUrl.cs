namespace Kiji.Markdown;

/// <summary>
/// Identifies content-local (relative) image URLs by extension. External URLs,
/// site-root references (<c>/...</c>, served from the static directory), and
/// non-image files are excluded.
/// </summary>
internal static class LocalImageUrl
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public static bool IsLocalImage(string url)
    {
        if (url.StartsWith('/') ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(url.AsSpan());
        foreach (var imageExtension in ImageExtensions)
        {
            if (extension.Equals(imageExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
