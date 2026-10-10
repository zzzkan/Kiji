namespace Kiji.Hosting;

/// <summary>A logical input, independent of the shared OS watcher covering it.</summary>
internal sealed class WatchedPath(string path, WatchedPathSource source, Func<string, bool>? affectsAsset)
{
    internal string Path { get; } = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    internal WatchedPathSource Source { get; } = source;

    internal string WatchRoot
    {
        get
        {
            // Keep watching when the input itself is deleted or replaced.
            var ancestor = Directory.GetParent(Path);
            while (ancestor is not null && !ancestor.Exists) { ancestor = ancestor.Parent; }
            return ancestor?.FullName ?? Path;
        }
    }

    internal bool Affects(string candidate, WatcherChangeTypes change)
    {
        // A rename/delete of an ancestor can move an entire source tree at once.
        if (change != WatcherChangeTypes.Changed && Contains(candidate, Path)) { return true; }
        return Contains(Path, candidate) && (affectsAsset is null || affectsAsset(candidate));
    }

    internal static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, child, comparison)
            || child.StartsWith(System.IO.Path.EndsInDirectorySeparator(parent) ? parent : parent + System.IO.Path.DirectorySeparatorChar, comparison);
    }
}
