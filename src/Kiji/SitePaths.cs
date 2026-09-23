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
        return Resolve(ResolveAgainstRoot(outputPath)) with
        {
            ImageCacheDirectory = Path.Combine(ResolveCachePath(), "images"),
        };
    }

    /// <summary>
    /// Paths for development and route planning. Only the dev server creates the mirror;
    /// the persistent publish cache is never used here.
    /// </summary>
    internal ResolvedSitePaths ResolveForDevelopment()
    {
        return Resolve(Path.Combine(ResolveKijiPath(), "dev-site"));
    }

    private ResolvedSitePaths Resolve(string outputPath)
    {
        return new ResolvedSitePaths
        {
            ContentDirectory = ResolveAgainstRoot(ContentDirectory),
            StaticDirectory = ResolveAgainstRoot(StaticDirectory),
            OutputDirectory = outputPath,
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
        static bool HasProject(DirectoryInfo directory)
        {
            return directory.EnumerateFiles("*.csproj").Any() || directory.EnumerateFiles("*.fsproj").Any();
        }
        static bool HasGit(DirectoryInfo directory)
        {
            return Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"));
        }

        return FindAncestor(appBaseDirectory, HasProject)
            ?? FindAncestor(currentDirectory, HasProject)
            ?? FindAncestor(appBaseDirectory, HasGit)
            ?? FindAncestor(currentDirectory, HasGit)
            ?? currentDirectory;
    }

    private static string? FindAncestor(string startPath, Func<DirectoryInfo, bool> matches)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (matches(directory))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
