namespace Kiji.Generation;

/// <summary>Protects source and cache files from output reconciliation.</summary>
internal static class OutputPathValidator
{
    internal static string ResolveUnderRoot(string root, string relativePath, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException(
                $"{description} '{relativePath}' must be relative to the output directory.");
        }

        var normalizedRoot = Normalize(root);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (string.Equals(normalizedRoot, fullPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Contains(normalizedRoot, fullPath))
        {
            throw new InvalidOperationException(
                $"{description} '{relativePath}' escapes the output directory.");
        }

        return fullPath;
    }

    internal static void Validate(ResolvedSitePaths options, Kiji.Assets.StaticAssetManifest assets, string? siteRoot = null, string? cachePath = null)
    {
        var output = Normalize(options.OutputDirectory);
        if (siteRoot is not null && Contains(output, Normalize(siteRoot)))
        {
            throw new InvalidOperationException("The output directory must not be the site root or one of its ancestors.");
        }

        foreach (var asset in assets.Assets)
        {
            if (Contains(output, Normalize(asset.Source)))
            {
                throw new InvalidOperationException($"Output directory '{output}' contains static asset source '{asset.Source}'. Choose a separate output directory.");
            }
        }

        foreach (var input in new[] { options.ContentDirectory, options.ImageCacheDirectory, cachePath,
            siteRoot is null ? null : Path.Combine(siteRoot, ".git") }.Concat(assets.DiscoveryRoots))
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
