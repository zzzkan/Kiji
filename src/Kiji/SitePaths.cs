namespace Kiji;

/// <summary>
/// Configurable site directory layout. Relative paths are resolved against <see cref="Root"/>.
/// </summary>
public sealed class SitePaths
{
    internal SitePaths(string root)
    {
        Root = root;
    }

    /// <summary>
    /// The site root directory. Defaults to the nearest ancestor directory containing <c>.git</c>.
    /// </summary>
    public string Root { get; set; }

    /// <summary>
    /// The content directory scanned for source files. Defaults to <c>contents</c>.
    /// </summary>
    public string Content { get; set; } = "contents";

    /// <summary>
    /// The static assets directory copied verbatim into the output.
    /// Defaults to <c>wwwroot</c> under <see cref="Root"/> when not set.
    /// </summary>
    public string? Static { get; set; }

    /// <summary>
    /// The output directory for the generated site. Defaults to <c>dist/wwwroot</c>.
    /// </summary>
    public string Output { get; set; } = Path.Combine("dist", "wwwroot");

    /// <summary>
    /// The subdirectory name under <see cref="Output"/> for generated content assets. Defaults to <c>_assets</c>.
    /// </summary>
    public string AssetsDirectoryName { get; set; } = "_assets";

    internal string ResolveOutputPath(string? outputOverride = null)
    {
        return ResolveAgainstRoot(outputOverride ?? Output);
    }

    internal string ResolveCachePath()
    {
        return Path.Combine(Path.GetFullPath(Root), ".kiji-cache");
    }

    internal SsgOptions ResolveForServe()
    {
        var cachePath = ResolveCachePath();
        Directory.CreateDirectory(cachePath);
        return ResolveForBuild() with { OutputPath = cachePath };
    }

    internal SsgOptions ResolveForBuild(string? outputOverride = null)
    {
        var staticPath = Static is not null
            ? ResolveAgainstRoot(Static)
            : Path.Combine(Path.GetFullPath(Root), "wwwroot");

        return new SsgOptions
        {
            ContentsPath = ResolveAgainstRoot(Content),
            StaticPath = staticPath,
            OutputPath = ResolveOutputPath(outputOverride),
            AssetsDirectoryName = AssetsDirectoryName,
        };
    }

    private string ResolveAgainstRoot(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Root, path));
    }
}
