namespace Kiji.SyntheticSite;

/// <summary>
/// Derives the route slug of a synthetic post from its markdown file location
/// (posts live at <c>contents/&lt;slug&gt;/index.md</c>).
/// </summary>
public static class PostSlug
{
    public static string From(FileInfo fileInfo)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);

        return string.Equals(fileInfo.Name, "index.md", StringComparison.OrdinalIgnoreCase)
            ? fileInfo.Directory?.Name
                ?? throw new InvalidOperationException($"Cannot determine a slug for '{fileInfo.FullName}'.")
            : Path.GetFileNameWithoutExtension(fileInfo.Name);
    }
}
