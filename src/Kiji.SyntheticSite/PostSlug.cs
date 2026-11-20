namespace Kiji.SyntheticSite;

public static class PostSlug
{
    public static string From(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var slug = string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(path))
            : Path.GetFileNameWithoutExtension(Path.GetFileName(path));
        return !string.IsNullOrWhiteSpace(slug)
            ? slug
            : throw new InvalidOperationException($"Cannot determine a slug for '{path}'.");
    }
}
