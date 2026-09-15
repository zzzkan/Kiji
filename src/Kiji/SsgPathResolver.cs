namespace Kiji;

/// <summary>
/// Resolves repository-scoped directories used by the static site generator.
/// </summary>
internal static class SsgPathResolver
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
            var directory = File.Exists(startPath)
                ? new FileInfo(startPath).Directory
                : new DirectoryInfo(startPath);

            while (directory is not null)
            {
                var marker = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        var formattedStartPaths = candidateStartPaths.Length > 0
            ? string.Join(", ", candidateStartPaths.Select(static path => $"'{path}'"))
            : "<none>";

        throw new DirectoryNotFoundException(
            $"Could not locate a repository root from the provided start paths ({formattedStartPaths}). " +
            "Expected an ancestor directory containing one of: '.git'.");
    }
}
