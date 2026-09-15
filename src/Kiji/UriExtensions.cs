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

    public static Uri AppendRelativePath(this Uri baseUri, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedBaseUri = baseUri.ToTrailingSlashUri();
        return new Uri(normalizedBaseUri.AbsoluteUri + relativePath.TrimStart('/'), UriKind.Absolute);
    }
}
