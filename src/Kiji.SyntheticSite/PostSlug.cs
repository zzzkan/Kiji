namespace Kiji.SyntheticSite;

/// <summary>
/// Derives the route slug of a synthetic post from its markdown file location
/// (posts live at <c>contents/&lt;slug&gt;/index.md</c>).
/// </summary>
public static class PostSlug
{
    public static string From(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(path))
                ?? throw new InvalidOperationException($"Cannot determine a slug for '{path}'.")
            : Path.GetFileNameWithoutExtension(Path.GetFileName(path));
    }
}
