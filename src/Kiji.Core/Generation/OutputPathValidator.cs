using Kiji.Paths;
using Kiji.StaticAssets;

namespace Kiji.Generation;

internal static class OutputPathValidator
{
    internal static void Validate(ResolvedSitePaths options, StaticAssetManifest assets, string? siteRoot = null, string? cachePath = null,
        IEnumerable<string>? inputPaths = null)
    {
        var output = PathContainment.Normalize(options.OutputDirectory);
        if (siteRoot is not null && PathContainment.Contains(output, PathContainment.Normalize(siteRoot)))
        {
            throw new InvalidOperationException("The output directory must not be the site root or one of its ancestors.");
        }

        foreach (var asset in assets.Assets)
        {
            if (PathContainment.Contains(output, PathContainment.Normalize(asset.Source)))
            {
                throw new InvalidOperationException($"Output directory '{output}' contains static asset source '{asset.Source}'. Choose a separate output directory.");
            }
        }

        var inputs = new[] { options.ImageCacheDirectory, cachePath,
            siteRoot is null ? null : Path.Combine(siteRoot, ".git") }
            .Concat(assets.DiscoveryRoots).Concat(inputPaths ?? [])
            .OfType<string>();
        ValidateInputs(output, inputs);
    }

    internal static void ValidateInputs(string outputPath, IEnumerable<string> inputPaths)
    {
        var output = PathContainment.Normalize(outputPath);
        foreach (var input in inputPaths.Select(PathContainment.Normalize)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (PathContainment.Contains(output, input) || PathContainment.Contains(input, output))
            {
                throw new InvalidOperationException($"Output directory '{output}' overlaps source or cache directory '{input}'. Choose a separate output directory.");
            }
        }
    }
}
