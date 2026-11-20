namespace Kiji.Markdown;

internal static class LocalImageUrl
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    public static bool IsLocalImage(string url)
    {
        if (url.StartsWith('/') || url.StartsWith('#') ||
            Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return false;
        }

        var extension = Path.GetExtension(ImageReferenceKey.FromMarkdownUrl(url).AsSpan());
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
