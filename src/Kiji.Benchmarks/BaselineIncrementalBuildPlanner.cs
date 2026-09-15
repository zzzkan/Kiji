using System.Collections.Concurrent;
using Kiji.Generation;

namespace Kiji.Benchmarks;

// Frozen pre-optimization scope hashing, limited to the measured code path.
internal sealed class BaselineIncrementalBuildPlanner(
    ResolvedSitePaths options, ContentFileRegistry? hashRegistry)
{
    private readonly ConcurrentDictionary<string, string> _fileFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _contentSetFingerprints = new(StringComparer.OrdinalIgnoreCase);
    internal string ContentSetFingerprint(string scope)
    {
        return _contentSetFingerprints.GetOrAdd(scope, static (key, self) => self.ComputeContentSetFingerprint(key), this);
    }

    private string ComputeContentSetFingerprint(string scope)
    {
        var scopePath = scope.Length == 0
            ? options.ContentDirectory
            : Path.GetFullPath(Path.Combine(options.ContentDirectory, scope));

        if (!Directory.Exists(scopePath))
        {
            return BuildFingerprint.Missing;
        }

        var files = ScanContentFiles(scopePath);

        var hashed = new (string RelativePath, string ContentHash)[files.Count];
        Parallel.For(0, files.Count, index =>
        {
            var file = files[index];
            hashed[index] = (
                Path.GetRelativePath(options.ContentDirectory, file.FullPath),
                HashFileCached(file.FullPath, (file.Length, file.LastWriteTimeUtc)));
        });

        return BuildFingerprint.HashFileSet(hashed);
    }

    /// <summary>
    /// The markdown files under a directory. Reuses the listing the content pass
    /// already walked when it covered exactly this directory; otherwise walks it.
    /// Enumerating <see cref="FileInfo"/> carries each stamp out of the walk, so the
    /// registry can validate its recorded hash without going back to disk.
    /// </summary>
    private IReadOnlyList<ScannedFile> ScanContentFiles(string directory)
    {
        if (hashRegistry?.GetScan(directory) is { } scanned)
        {
            return scanned;
        }

        return
        [
            .. new DirectoryInfo(directory)
                .EnumerateFiles("*.md", SearchOption.AllDirectories)
                .Select(static file => new ScannedFile(file.FullName, file.Length, file.LastWriteTimeUtc)),
        ];
    }

    internal string HashFileCached(string path, (long Length, DateTime LastWriteTimeUtc)? stamp = null)
    {
        // Content files parsed during materialization already carry a stamp-validated
        // hash in the registry; only files nobody read yet are hashed from disk.
        return _fileFingerprints.GetOrAdd(
            Path.GetFullPath(path),
            static (fullPath, state) =>
                state.Registry?.GetValidatedHash(fullPath, state.Stamp) ?? BuildFingerprint.HashFile(fullPath),
            (Registry: hashRegistry, Stamp: stamp));
    }

}
