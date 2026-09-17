namespace Kiji;

internal static class UriExtensions
{
    public static Uri ToTrailingSlashUri(this Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The URI must be absolute.", nameof(uri));
        }

        var absoluteUri = uri.AbsoluteUri;
        return absoluteUri.EndsWith('/')
            ? uri
            : new Uri(absoluteUri + '/', UriKind.Absolute);
    }

}
