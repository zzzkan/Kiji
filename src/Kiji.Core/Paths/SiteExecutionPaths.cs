using System.Reflection;

namespace Kiji.Paths;

internal sealed class SiteExecutionPaths
{
    internal SiteExecutionPaths(string projectDirectory, string? assetManifestBasePath = null)
    {
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        AssetManifestBasePath = assetManifestBasePath;
    }

    internal string ProjectDirectory { get; }
    internal string? AssetManifestBasePath { get; }

    internal static SiteExecutionPaths FromRuntimeMetadata(SiteRuntimeMetadata runtimeMetadata, Assembly entryAssembly)
    {
        if (runtimeMetadata.ProjectDirectory is not string directory || string.IsNullOrWhiteSpace(directory)
            || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "Kiji.ProjectDirectory is missing or invalid. Ensure the site imports Kiji's MSBuild targets and rebuild the project before running it.");
        }

        return new SiteExecutionPaths(directory, Path.Combine(AppContext.BaseDirectory,
            entryAssembly.GetName().Name!));
    }

    internal string ResolveKijiPath()
    {
        return Path.Combine(ProjectDirectory, ".kiji");
    }

    internal string ResolveCachePath()
    {
        return Path.Combine(ResolveKijiPath(), "cache");
    }

    internal string ResolveImageWorkPath(ResolvedSitePaths options)
    {
        return options.ImageCacheDirectory is null
            ? Path.Combine(ResolveKijiPath(), "dev-work")
            : Path.Combine(ResolveCachePath(), "work");
    }

    internal ResolvedSitePaths ResolveForPublish(string outputPath)
    {
        return new ResolvedSitePaths
        {
            OutputDirectory = ResolveAgainstRoot(outputPath),
            ImageCacheDirectory = Path.Combine(ResolveCachePath(), "images"),
            AssetManifestPath = FindAssetManifest("staticwebassets.publish.runtime.json"),
            AssetEndpointsPath = FindAssetManifest("staticwebassets.publish.endpoints.json"),
        };
    }

    internal ResolvedSitePaths ResolveForDevelopment()
    {
        return new ResolvedSitePaths
        {
            OutputDirectory = Path.Combine(ResolveKijiPath(), "dev-site"),
            AssetManifestPath = FindAssetManifest("staticwebassets.runtime.json"),
            AssetEndpointsPath = FindAssetManifest("staticwebassets.endpoints.json"),
        };
    }

    private string? FindAssetManifest(string suffix)
    {
        return AssetManifestBasePath is null ? null : $"{AssetManifestBasePath}.{suffix}";
    }

    internal string ResolveAgainstRoot(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ProjectDirectory, path));
    }
}
