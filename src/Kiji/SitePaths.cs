namespace Kiji;

/// <summary>
/// Configurable site directory layout. Relative paths are resolved against <see cref="Root"/>.
/// </summary>
/// <remarks>
/// There is deliberately no output directory here. <c>dotnet publish</c> decides where the
/// generated site goes, and Kiji's MSBuild targets hand that path to the app — so the
/// publish directory is the single source of truth for it.
/// </remarks>
public sealed class SitePaths
{
    internal SitePaths(string root)
    {
        Root = root;
    }

    /// <summary>
    /// The site root directory, which <see cref="Content"/> and <see cref="Static"/> resolve
    /// against. Defaults to the site's own project directory (the nearest ancestor containing
    /// a project file), falling back to the nearest ancestor containing <c>.git</c> and then
    /// to the current directory.
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

    internal string ResolveKijiPath()
    {
        return Path.Combine(Path.GetFullPath(Root), ".kiji");
    }

    internal string ResolveCachePath()
    {
        return Path.Combine(ResolveKijiPath(), "cache");
    }

    /// <summary>
    /// Paths for a publish: the site is written to <paramref name="outputPath"/>, which
    /// <c>dotnet publish</c> supplied.
    /// </summary>
    internal SsgOptions ResolveForPublish(string outputPath)
    {
        return Resolve(ResolveAgainstRoot(outputPath));
    }

    /// <summary>
    /// Paths for the dev server: pages render on demand into a mirror under <c>.kiji</c>,
    /// never into a publish directory.
    /// </summary>
    internal SsgOptions ResolveForServe()
    {
        var siteMirrorPath = ResolveSiteMirrorPath();
        Directory.CreateDirectory(siteMirrorPath);
        return Resolve(siteMirrorPath);
    }

    /// <summary>
    /// Paths for expanding routes without producing anything — the fallback when page
    /// planning runs before a command settled the options. Points at the same mirror as
    /// <see cref="ResolveForServe"/> so nothing can be mistaken for a deliverable, and
    /// creates no directories.
    /// </summary>
    internal SsgOptions ResolveForPlanning()
    {
        return Resolve(ResolveSiteMirrorPath());
    }

    private string ResolveSiteMirrorPath()
    {
        return Path.Combine(ResolveCachePath(), "site");
    }

    private SsgOptions Resolve(string outputPath)
    {
        var staticPath = Static is not null
            ? ResolveAgainstRoot(Static)
            : Path.Combine(Path.GetFullPath(Root), "wwwroot");

        return new SsgOptions
        {
            ContentsPath = ResolveAgainstRoot(Content),
            StaticPath = staticPath,
            OutputPath = outputPath,
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
