namespace Kiji;

internal static class RelativePath
{
    internal static void Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.StartsWith('/')
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('?', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal)
            || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "The path must be relative to the site base URL and cannot contain a leading slash, an absolute URI, a query, a fragment, or a backslash.",
                parameterName);
        }
    }
}
