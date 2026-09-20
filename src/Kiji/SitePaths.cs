namespace Kiji;

/// <summary>Configurable input directories, frozen when site execution starts.</summary>
public sealed class SitePaths
{
    private bool _frozen;
    internal SitePaths(string root)
    {
        RootDirectory = root;
    }

    /// <summary>The root against which relative site paths are resolved.</summary>
    /// <remarks>Defaults to the nearest project directory, then the nearest Git directory, then the current directory.</remarks>
    public string RootDirectory
    {
        get;
        set { EnsureMutable(); field = value; }
    }

    /// <summary>The content directory relative to the root, defaulting to <c>contents</c>.</summary>
    public string ContentDirectory
    {
        get;
        set { EnsureMutable(); field = value; }
    } = "contents";

    /// <summary>The static assets directory relative to the root, defaulting to <c>wwwroot</c>.</summary>
    public string StaticDirectory
    {
        get;
        set { EnsureMutable(); field = value; }
    } = "wwwroot";

    internal void Freeze() => _frozen = true;

    private void EnsureMutable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Site paths cannot be changed after execution has started.");
        }
    }

    internal string ResolveKijiPath()
    {
        return Path.Combine(Path.GetFullPath(RootDirectory), ".kiji");
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
        return Resolve(ResolveAgainstRoot(outputPath));
    }

    /// <summary>
    /// Paths for the dev server: pages render on demand into a mirror under <c>.kiji</c>,
    /// never into a publish directory.
    /// </summary>
    internal ResolvedSitePaths ResolveForServe()
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
    internal ResolvedSitePaths ResolveForPlanning()
    {
        return Resolve(ResolveSiteMirrorPath());
    }

    private string ResolveSiteMirrorPath()
    {
        return Path.Combine(ResolveCachePath(), "site");
    }

    private ResolvedSitePaths Resolve(string outputPath)
    {
        return new ResolvedSitePaths
        {
            ContentDirectory = ResolveAgainstRoot(ContentDirectory),
            StaticDirectory = ResolveAgainstRoot(StaticDirectory),
            OutputDirectory = outputPath,
            ImageCacheDirectory = Path.Combine(ResolveCachePath(), "images"),
        };
    }

    private string ResolveAgainstRoot(string path)
    {
        return Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(RootDirectory, path));
    }

    internal static string ResolveDefaultRoot(string appBaseDirectory, string currentDirectory)
    {
        var projectRoot = FindNearestProjectDirectory(appBaseDirectory)
            ?? FindNearestProjectDirectory(currentDirectory);
        if (projectRoot is not null)
        {
            return projectRoot;
        }

        try
        {
            return SsgPathResolver.ResolveRepositoryRoot(appBaseDirectory, currentDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            return currentDirectory;
        }
    }

    private static string? FindNearestProjectDirectory(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any() || directory.EnumerateFiles("*.fsproj").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
