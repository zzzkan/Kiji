using System.Collections.Concurrent;
using System.Text;

namespace Kiji.Generation;

/// <summary>Resolves images within the same registered roots used to load and watch content.</summary>
internal sealed class ContentPath(IEnumerable<string> roots)
{
    internal string[] Roots { get; } = [.. roots.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];

    private ConcurrentDictionary<string, Dictionary<string, string>> _directories = new(StringComparer.Ordinal);

    internal void Invalidate() => Interlocked.Exchange(ref _directories, new(StringComparer.Ordinal));

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
        var directories = Volatile.Read(ref _directories);
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            // Cache successful listings only. A miss refreshes the listing, so a file
            // created before the watcher's debounce completes can already be resolved.
            var entries = directories.GetOrAdd(current, ReadNames);
            entries.TryGetValue(segment, out var name);
            if (name is null)
            {
                entries = ReadNames(current);
                directories[current] = entries;
                name = FindName(entries, segment) ?? segment; // e.g. Windows 8.3 aliases
            }
            current = Path.Combine(current, name);
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

    private static Dictionary<string, string> ReadNames(string directory) =>
        Directory.EnumerateFileSystemEntries(directory).Select(static path => Path.GetFileName(path))
            .ToDictionary(static name => name, StringComparer.Ordinal);

    private static string Normalize(string name)
    {
        try { return name.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return name; } // NTFS permits unpaired UTF-16 surrogates.
    }

    private static string? FindName(Dictionary<string, string> entries, string segment)
    {
        if (entries.TryGetValue(segment, out var exact)) { return exact; }
        var normalized = Normalize(segment);
        var matches = entries.Keys.Where(name => string.Equals(Normalize(name),
            normalized, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
