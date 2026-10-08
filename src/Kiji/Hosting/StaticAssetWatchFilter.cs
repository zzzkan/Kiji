using Kiji.Assets;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Kiji.Hosting;

// ContentRoots are physical lookup roots, not declarations that every file below
// them is public (a collocated .razor.js can put the whole project in a root).
internal sealed class StaticAssetWatchFilter
{
    private readonly string _root;
    private readonly HashSet<string> _files;
    private readonly HashSet<string> _directories;
    private readonly Matcher _discovery = new();
    private readonly bool _hasDiscovery;
    private readonly Lock _lock = new();

    internal StaticAssetWatchFilter(string root, StaticAssetManifest manifest)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _files = new(comparer);
        _directories = new(comparer);
        foreach (var asset in manifest.Assets)
        {
            if (IsUnderRoot(asset.Source)) { Remember(asset.Source); }
        }
        foreach (var (patternRoot, pattern) in manifest.DiscoveryPatterns)
        {
            if (comparer.Equals(_root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(patternRoot))))
            {
                _discovery.AddInclude(pattern);
                _hasDiscovery = true;
            }
        }
        if (_hasDiscovery && Directory.Exists(_root))
        {
            foreach (var file in _discovery.GetResultsInFullPath(_root)) { Remember(file); }
        }
    }

    internal bool AffectsAsset(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        lock (_lock)
        {
            // Retain ancestor paths after deletion: a directory move/delete may
            // emit no individual file events, and replacing it must still reload.
            if (_files.Contains(path) || _directories.Contains(path)) { return true; }
            if (!_hasDiscovery || !IsUnderRoot(path)) { return false; }
            if (Directory.Exists(path))
            {
                var found = false;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (!_discovery.Match(Path.GetRelativePath(_root, file)).HasMatches) { continue; }
                        Remember(file);
                        found = true;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Directory replacement can race discovery. Reload when its
                    // contents cannot be determined instead of losing the event.
                    return true;
                }
                return found;
            }
            if (!_discovery.Match(Path.GetRelativePath(_root, path)).HasMatches) { return false; }
            Remember(path);
            return true;
        }
    }

    private bool IsUnderRoot(string path) => path.StartsWith(_root + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void Remember(string file)
    {
        _files.Add(file);
        for (var directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            _directories.Add(directory);
            if (!IsUnderRoot(directory)) { break; }
        }
    }
}
