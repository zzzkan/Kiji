using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>Portable file identities shared by rendering and incremental publication.</summary>
internal sealed class ProjectPath
{
    private ConcurrentDictionary<string, Lazy<Dictionary<string, string>>> _directories = new(StringComparer.Ordinal);

    internal void Invalidate() => Interlocked.Exchange(ref _directories, new(StringComparer.Ordinal));

    internal static string? GetRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        return BuildManifest.IsRelativeOutput(relative) ? relative : null;
    }

    internal string ResolveFile(string root, string path)
    {
        var relative = GetRelativePath(root, path)
            ?? throw new InvalidOperationException("Image sources must be inside the project directory.");
        if (!File.Exists(path)) { throw new FileNotFoundException("Image source was not found.", path); }

        // Enumerated names reflect the actual filesystem casing, even when a reference
        // uses a case alias on Windows, macOS, or a case-insensitive mounted volume.
        var directories = Volatile.Read(ref _directories);
        var current = root;
        foreach (var segment in relative.Split('/'))
        {
            // Names belong to the content snapshot. Enumerate each parent once,
            // rather than scan all article directories for every image reference.
            var entries = directories.GetOrAdd(current, static directory => new(() =>
                Directory.EnumerateFileSystemEntries(directory).Select(static path => Path.GetFileName(path))
                    .ToDictionary(static name => name, StringComparer.Ordinal))).Value;
            var name = entries.TryGetValue(segment, out var exact) ? exact
                : entries.Keys.Single(item => string.Equals(item, segment, StringComparison.OrdinalIgnoreCase));
            current = Path.Combine(current, name);
            // Watching an alias does not watch its target. Reject links rather than
            // publish files whose changes cannot invalidate the content snapshot.
            // Attributes stay live: a cached name must never conceal a new link.
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Image sources cannot traverse symbolic links or junctions.");
            }
        }
        return current;
    }
}
