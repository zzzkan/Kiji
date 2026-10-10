using System.Reflection;

namespace Kiji;

/// <summary>Fixed project and execution paths supplied by the site's build.</summary>
internal sealed class SiteExecutionPaths
{
    internal SiteExecutionPaths(string projectDirectory, string? assetManifestBasePath = null)
    {
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        AssetManifestBasePath = assetManifestBasePath;
    }

    internal string ProjectDirectory { get; }
    internal string? AssetManifestBasePath { get; }

    internal static SiteExecutionPaths FromRuntimeMetadata(object? value)
    {
        if (value is not string directory || string.IsNullOrWhiteSpace(directory)
            || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "Kiji.ProjectDirectory is missing or invalid. Ensure the site imports Kiji's MSBuild targets and rebuild the project before running it.");
        }

        return new SiteExecutionPaths(directory, Path.Combine(AppContext.BaseDirectory,
            Assembly.GetEntryAssembly()!.GetName().Name!));
    }

    internal string ResolveKijiPath()
    {
        return Path.Combine(ProjectDirectory, ".kiji");
    }

    internal string ResolveCachePath()
    {
        return Path.Combine(ResolveKijiPath(), "cache");
    }

    /// <summary>
    /// Paths for a publish: the site is written to <paramref name="outputPath"/>, which
    /// <c>dotnet publish</c> supplied.
    /// </summary>
    internal ResolvedSitePaths ResolveForPublish(string outputPath)
    {
        return Resolve(ResolveAgainstRoot(outputPath)) with
        {
            ImageCacheDirectory = Path.Combine(ResolveCachePath(), "images"),
            AssetManifestPath = FindAssetManifest("staticwebassets.publish.runtime.json"),
            AssetEndpointsPath = FindAssetManifest("staticwebassets.publish.endpoints.json"),
        };
    }

    /// <summary>
    /// Paths for development and route planning. Only the dev server creates the mirror;
    /// the persistent publish cache is never used here.
    /// </summary>
    internal ResolvedSitePaths ResolveForDevelopment()
    {
        return Resolve(Path.Combine(ResolveKijiPath(), "dev-site")) with
        {
            AssetManifestPath = FindAssetManifest("staticwebassets.runtime.json"),
            AssetEndpointsPath = FindAssetManifest("staticwebassets.endpoints.json"),
        };
    }

    private string? FindAssetManifest(string suffix)
    {
        return AssetManifestBasePath is null ? null : $"{AssetManifestBasePath}.{suffix}";
    }

    private static ResolvedSitePaths Resolve(string outputPath)
    {
        return new ResolvedSitePaths
        {
            OutputDirectory = outputPath,
        };
    }

    internal string ResolveAgainstRoot(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(ProjectDirectory, path));
    }
}
