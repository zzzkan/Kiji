namespace Kiji.Generation;

/// <summary>Resolves images within the same registered roots used to load and watch content.</summary>
internal sealed class ContentPath(IEnumerable<string> roots)
{
    internal string[] Roots { get; } = [.. roots.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];

    internal static string? GetRelativePath(string root, string path) =>
        OutputPathValidator.Contains(root, Path.GetFullPath(path))
            ? Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') : null;

    internal string ResolveFile(string path)
    {
        path = Path.GetFullPath(path);
        var root = Roots.Where(root => OutputPathValidator.Contains(root, path)).MaxBy(root => root.Length)
            ?? throw new InvalidOperationException($"Image source '{path}' must be inside a registered content directory.");
        if (!File.Exists(path)) { throw new FileNotFoundException($"Image source was not found at '{path}'.", path); }

        // A registered root may itself be a link, just as for Markdown loading and
        // watching. Redirecting descendants below that watched root are rejected.
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            // Let the filesystem resolve case, Unicode, and short-name aliases.
            // Public variant names are normalized by the image processor.
            current = Path.Combine(current, segment);
            var attributes = File.GetAttributes(current);
            // Reparse points also include OneDrive placeholders and deduplicated files.
            // LinkTarget identifies redirects without rejecting those ordinary files.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current) : new FileInfo(current);
                if (entry.LinkTarget is not null)
                {
                    throw new InvalidOperationException($"Image source '{path}' cannot traverse symbolic links or junctions below a content root.");
                }
            }
        }
        return current;
    }
}
