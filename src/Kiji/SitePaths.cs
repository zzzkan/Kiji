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
    /// The site root directory, which <see cref="Content"/>, <see cref="Static"/>, and
    /// <see cref="Output"/> resolve against. Defaults to the site's own project directory
    /// (the nearest ancestor containing a project file), falling back to the nearest
    /// ancestor containing <c>.git</c> and then to the current directory.
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
    /// The output directory for the generated site. Defaults to <c>dist</c>.
    /// </summary>
    public string Output { get; set; } = "dist";

    internal string ResolveOutputPath()
    {
        return ResolveAgainstRoot(Output);
    }

    internal string ResolveKijiPath()
    {
        return Path.Combine(Path.GetFullPath(Root), ".kiji");
    }

    internal string ResolveCachePath()
    {
        return Path.Combine(ResolveKijiPath(), "cache");
    }

    internal SsgOptions ResolveForServe()
    {
        var cachePath = ResolveCachePath();
        var siteMirrorPath = Path.Combine(cachePath, "site");
        Directory.CreateDirectory(siteMirrorPath);
        return ResolveForBuild() with { OutputPath = siteMirrorPath };
    }

    internal SsgOptions ResolveForBuild()
    {
        var staticPath = Static is not null
            ? ResolveAgainstRoot(Static)
            : Path.Combine(Path.GetFullPath(Root), "wwwroot");

        return new SsgOptions
        {
            ContentsPath = ResolveAgainstRoot(Content),
            StaticPath = staticPath,
            OutputPath = ResolveOutputPath(),
            ImageCachePath = Path.Combine(ResolveCachePath(), "images"),
        };
    }

    private string ResolveAgainstRoot(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Root, path));
    }
}
