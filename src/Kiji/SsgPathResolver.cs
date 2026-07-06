namespace Kiji;

/// <summary>
/// Resolves repository-scoped directories used by the static site generator.
/// </summary>
public static class SsgPathResolver
{
    /// <summary>
    /// Resolves the nearest repository root from one or more starting paths.
    /// </summary>
    public static string ResolveRepositoryRoot(params string[] startPaths)
    {
        ArgumentNullException.ThrowIfNull(startPaths);

        var candidateStartPaths = startPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var startPath in candidateStartPaths)
        {
            var repositoryRoot = FindAncestorDirectory(startPath, IsRepositoryRootDirectory);
            if (repositoryRoot is not null)
            {
                return repositoryRoot;
            }
        }

        var formattedStartPaths = candidateStartPaths.Length > 0
            ? string.Join(", ", candidateStartPaths.Select(static path => $"'{path}'"))
            : "<none>";

        throw new DirectoryNotFoundException(
            $"Could not locate a repository root from the provided start paths ({formattedStartPaths}). " +
            "Expected an ancestor directory containing one of: '.git'.");
    }

    private static bool IsRepositoryRootDirectory(string directory)
    {
        return Directory.Exists(Path.Combine(directory, ".git"));
    }

    private static string? FindAncestorDirectory(string startPath, Func<string, bool> predicate)
    {
        var fullPath = Path.GetFullPath(startPath);
        var directory = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : File.Exists(fullPath)
                ? new FileInfo(fullPath).Directory
                : new DirectoryInfo(fullPath);

        while (directory is not null)
        {
            if (predicate(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
