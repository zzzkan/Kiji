namespace Kiji.Generation;

/// <summary>Portable file identities shared by rendering and incremental publication.</summary>
internal static class ProjectPath
{
    internal static string? GetRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        return BuildManifest.IsRelativeOutput(relative) ? relative : null;
    }

    internal static string ResolveFile(string root, string path)
    {
        var relative = GetRelativePath(root, path)
            ?? throw new InvalidOperationException("Image sources must be inside the project directory.");
        if (!File.Exists(path)) { throw new FileNotFoundException("Image source was not found.", path); }

        // Enumerated names reflect the actual filesystem casing, even when a reference
        // uses a case alias on Windows, macOS, or a case-insensitive mounted volume.
        var current = root;
        foreach (var segment in relative.Split('/'))
        {
            var entries = new DirectoryInfo(current).EnumerateFileSystemInfos(segment,
                new EnumerationOptions { AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple }).ToArray();
            var entry = entries.FirstOrDefault(item => item.Name == segment)
                ?? entries.Single(item => string.Equals(item.Name, segment, StringComparison.OrdinalIgnoreCase));
            // Watching an alias does not watch its target. Reject links rather than
            // publish files whose changes cannot invalidate the content snapshot.
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Image sources cannot traverse symbolic links or junctions.");
            }
            current = entry.FullName;
        }
        return current;
    }
}
