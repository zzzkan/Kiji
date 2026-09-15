namespace Kiji.Generation;

/// <summary>Protects source and cache files from output reconciliation.</summary>
internal static class OutputPathValidator
{
    internal static void Validate(ResolvedSitePaths options, string? siteRoot = null, string? cachePath = null)
    {
        var output = Normalize(options.OutputDirectory);
        if (siteRoot is not null && Contains(output, Normalize(siteRoot)))
        {
            throw new InvalidOperationException("The output directory must not be the site root or one of its ancestors.");
        }

        foreach (var input in new[] { options.ContentDirectory, options.StaticDirectory, options.ImageCacheDirectory, cachePath,
            siteRoot is null ? null : Path.Combine(siteRoot, ".git") })
        {
            if (input is not null && (Contains(output, Normalize(input)) || Contains(Normalize(input), output)))
            {
                throw new InvalidOperationException($"Output directory '{output}' overlaps source or cache directory '{input}'. Choose a separate output directory.");
            }
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, child, comparison)
            || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison);
    }
}
