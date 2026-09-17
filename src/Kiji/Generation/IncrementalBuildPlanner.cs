using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// Drives the incremental build: loads the previous manifest, fingerprints the
/// current inputs (options, assemblies, content files), decides per page whether the
/// existing output is still valid, syncs static files by stamp, reconciles the output
/// directory against what the build produced, and writes the new manifest. Every
/// ambiguous situation falls back to re-rendering — a stale output is never acceptable.
/// </summary>
internal sealed class IncrementalBuildPlanner(
    ResolvedSitePaths options,
    string rootPath,
    string cacheDirectory,
    SiteInfo site,
    IReadOnlyList<string> buildInputPaths,
    IReadOnlyList<KeyValuePair<string, string>> buildInputValues,
    ContentFileRegistry? hashRegistry = null)
{
    private readonly string _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    private readonly string _manifestPath = Path.Combine(cacheDirectory, "build-manifest.json");
    private readonly ConcurrentDictionary<string, Lazy<string>> _fileFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<string>> _contentSetFingerprints = new(StringComparer.OrdinalIgnoreCase);
    internal Action<string>? BeforeContentSetHash { get; init; }
    internal Action<string>? BeforeFileHash { get; init; }

    internal async Task<IncrementalBuildPlan> CreatePlanAsync(
        IReadOnlyList<PageRenderRequest> pages,
        IEnumerable<Assembly> assemblies,
        bool force,
        CancellationToken cancellationToken)
    {
        var optionsHash = ComputeOptionsHash();
        var assemblyMvids = CollectAssemblyMvids(assemblies);

        var oldManifest = force ? null : await LoadManifestAsync(cancellationToken);

        // Without a manifest nothing about the previous build can be proven, so every
        // page renders. Unrecognized files in the output directory are not a reason to
        // re-render anything: the reconciliation pass at the end of the build deletes
        // whatever this build did not produce.
        if (oldManifest is null
            || oldManifest.OptionsHash != optionsHash
            || !oldManifest.AssemblyMvids.SequenceEqual(assemblyMvids, StringComparer.Ordinal))
        {
            return new IncrementalBuildPlan(pages, [], oldManifest, optionsHash, assemblyMvids);
        }

        var oldPages = oldManifest.Pages.ToDictionary(
            static page => page.OutputRelativePath,
            StringComparer.OrdinalIgnoreCase);

        var decisions = new (PageRenderRequest? Render, BuildManifestPage? Carried)[pages.Count];
        Parallel.For(0, pages.Count, index =>
        {
            var request = pages[index];
            decisions[index] = oldPages.TryGetValue(request.OutputRelativePath, out var oldPage)
                && CanSkip(request, oldPage)
                    ? (null, oldPage)
                    : (request, null);
        });

        var pagesToRender = new List<PageRenderRequest>();
        var carriedPages = new List<BuildManifestPage>();
        foreach (var (render, carried) in decisions)
        {
            if (render is not null)
            {
                pagesToRender.Add(render);
            }
            else
            {
                carriedPages.Add(carried!);
            }
        }

        return new IncrementalBuildPlan(pagesToRender, carriedPages, oldManifest, optionsHash, assemblyMvids);
    }

    private bool CanSkip(PageRenderRequest request, BuildManifestPage oldPage)
    {
        if (!string.Equals(oldPage.RoutePath, request.RoutePath, StringComparison.Ordinal)
            || !string.Equals(oldPage.ParametersHash, BuildFingerprint.HashParameters(request.Parameters), StringComparison.Ordinal))
        {
            return false;
        }

        // The existing output (and everything the page materialized beside it) must
        // still be exactly what the previous build wrote. A matching stamp
        // (length + last write time) lets the recorded hash be trusted without
        // re-reading the file; on any stamp mismatch the hash is recomputed.
        var outputPath = Path.Combine(options.OutputDirectory, oldPage.OutputRelativePath);
        if (!StampMatches(outputPath, oldPage.OutputLength, oldPage.OutputLastWriteTimeUtc)
            && !string.Equals(HashFileCached(outputPath), oldPage.OutputHash, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var additionalOutput in oldPage.AdditionalOutputs)
        {
            if (!File.Exists(Path.Combine(options.OutputDirectory, additionalOutput)))
            {
                return false;
            }
        }

        foreach (var dependency in oldPage.Dependencies)
        {
            switch (dependency.Kind)
            {
                case BuildManifestDependency.FileKind:
                    var dependencyPath = ResolveDependencyPath(dependency.Key);
                    if (StampMatches(dependencyPath, dependency.Length, dependency.LastWriteTimeUtc))
                    {
                        continue;
                    }

                    if (!string.Equals(HashFileCached(dependencyPath), dependency.Fingerprint, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    break;

                case BuildManifestDependency.ContentSetKind:
                    if (!string.Equals(ContentSetFingerprint(dependency.Key), dependency.Fingerprint, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    break;

                default:
                    return false; // Unknown kind from a newer schema: re-render.
            }
        }

        return true;
    }

    private static bool StampMatches(string path, long? length, DateTime? lastWriteTimeUtc)
    {
        if (length is null || lastWriteTimeUtc is null)
        {
            return false;
        }

        var info = new FileInfo(path);
        return info.Exists && info.Length == length && info.LastWriteTimeUtc == lastWriteTimeUtc;
    }

    private static (long Length, DateTime LastWriteTimeUtc)? ReadStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc) : null;
    }

    internal BuildManifestPage CreatePageEntry(
        PageRenderRequest request,
        string outputHash,
        BuildDependencyRecorder recorder)
    {
        // The recorder hands back sorted snapshots, so the manifest's order is settled
        // without a LINQ chain per page.
        var files = recorder.Files;
        var scopes = recorder.ContentSetScopes;
        var dependencies = new List<BuildManifestDependency>(files.Length + scopes.Length);

        foreach (var file in files)
        {
            var stamp = ReadStamp(file);
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.FileKind,
                ToDependencyKey(file),
                HashFileCached(file, stamp),
                stamp?.Length,
                stamp?.LastWriteTimeUtc));
        }

        foreach (var scope in scopes)
        {
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.ContentSetKind,
                scope,
                ContentSetFingerprint(scope)));
        }

        var additionalOutputs = recorder.AdditionalOutputs;
        for (var i = 0; i < additionalOutputs.Length; i++)
        {
            additionalOutputs[i] = Path.GetRelativePath(options.OutputDirectory, additionalOutputs[i]);
        }

        Array.Sort(additionalOutputs, StringComparer.OrdinalIgnoreCase);

        var outputStamp = ReadStamp(Path.Combine(options.OutputDirectory, request.OutputRelativePath));
        return new BuildManifestPage(
            request.OutputRelativePath,
            request.RoutePath,
            BuildFingerprint.HashParameters(request.Parameters),
            outputHash,
            dependencies,
            additionalOutputs,
            outputStamp?.Length,
            outputStamp?.LastWriteTimeUtc);
    }

    /// <summary>
    /// Copies static files whose source or destination stamp changed since the last
    /// build; untouched files are skipped entirely.
    /// </summary>
    internal async Task<IReadOnlyList<BuildManifestStaticFile>> SyncStaticFilesAsync(IncrementalBuildPlan plan)
    {
        if (!Directory.Exists(options.StaticDirectory))
        {
            return [];
        }

        var oldEntries = (plan.OldManifest?.StaticFiles ?? [])
            .ToDictionary(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        var files = new DirectoryInfo(options.StaticDirectory).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        var entries = new BuildManifestStaticFile[files.Length];
        var copied = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            (index, _) =>
            {
                var source = files[index];
                var relativePath = Path.GetRelativePath(options.StaticDirectory, source.FullName);
                var destinationPath = Path.Combine(options.OutputDirectory, relativePath);

                if (oldEntries.TryGetValue(relativePath, out var oldEntry)
                    && oldEntry.SourceLength == source.Length
                    && oldEntry.SourceLastWriteTimeUtc == source.LastWriteTimeUtc)
                {
                    var destination = new FileInfo(destinationPath);
                    if (destination.Exists
                        && destination.Length == oldEntry.DestinationLength
                        && destination.LastWriteTimeUtc == oldEntry.DestinationLastWriteTimeUtc)
                    {
                        entries[index] = oldEntry;
                        return ValueTask.CompletedTask;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(source.FullName, destinationPath, overwrite: true);
                Interlocked.Increment(ref copied);

                var copiedInfo = new FileInfo(destinationPath);
                entries[index] = new BuildManifestStaticFile(
                    relativePath,
                    source.Length,
                    source.LastWriteTimeUtc,
                    copiedInfo.Length,
                    copiedInfo.LastWriteTimeUtc);
                return ValueTask.CompletedTask;
            });

        if (copied > 0 || files.Length > 0)
        {
            BuildOutput.Info($"Static files: {copied} copied, {files.Length - copied} unchanged.");
        }

        return entries;
    }

    /// <summary>
    /// Makes the output directory hold exactly what this build produced: every file it
    /// does not claim is deleted, and directories left empty are pruned.
    /// </summary>
    /// <remarks>Also removes files absent from the previous manifest.</remarks>
    internal void ReconcileOutputs(BuildManifest manifest)
    {
        if (!Directory.Exists(options.OutputDirectory))
        {
            return;
        }

        var expected = CollectOutputRelativePaths(manifest);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(options.OutputDirectory, "*", SearchOption.AllDirectories))
        {
            if (expected.Contains(Path.GetRelativePath(options.OutputDirectory, file)))
            {
                continue;
            }

            File.Delete(file);
            removed++;
        }

        if (removed > 0)
        {
            PruneEmptyDirectories(options.OutputDirectory);
            BuildOutput.Info($"Removed {removed} file(s) the build did not produce.");
        }
    }

    internal async Task SaveManifestAsync(BuildManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);

        // Straight to the file: a thousand-page manifest serialized to a string first
        // would be half a megabyte of UTF-16 that then has to be transcoded on the way out.
        var stream = new FileStream(_manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using (stream)
        {
            await JsonSerializer.SerializeAsync(
                stream,
                manifest,
                BuildManifestJsonContext.Default.BuildManifest,
                cancellationToken);
        }
    }

    internal async Task<BuildManifest?> LoadManifestAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_manifestPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize(json, BuildManifestJsonContext.Default.BuildManifest);
            return manifest?.IsValid() == true ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The digest of every <c>*.md</c> under a content-set scope — a contents-relative
    /// directory, or empty for the whole tree. Computed once per scope per build.
    /// </summary>
    internal string ContentSetFingerprint(string scope)
    {
        return _contentSetFingerprints.GetOrAdd(scope, static (key, self) =>
            new Lazy<string>(() => self.ComputeContentSetFingerprint(key), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
    }

    private string ComputeContentSetFingerprint(string scope)
    {
        BeforeContentSetHash?.Invoke(scope);
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
                Path.GetRelativePath(options.ContentDirectory, file.FullName),
                HashFileCached(file.FullName, (file.Length, file.LastWriteTimeUtc)));
        });

        return BuildFingerprint.HashFileSet(hashed);
    }

    /// <summary>
    /// The markdown files under a directory. Reuses the listing the content pass
    /// already walked when it covered exactly this directory; otherwise walks it.
    /// Enumerating <see cref="FileInfo"/> carries each stamp out of the walk, so the
    /// registry can validate its recorded hash without going back to disk.
    /// </summary>
    private IReadOnlyList<FileInfo> ScanContentFiles(string directory)
    {
        if (hashRegistry?.GetScan(directory) is { } scanned)
        {
            return scanned;
        }

        return [.. new DirectoryInfo(directory).EnumerateFiles("*.md", SearchOption.AllDirectories)];
    }

    private static HashSet<string> CollectOutputRelativePaths(BuildManifest manifest)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in manifest.Pages)
        {
            paths.Add(page.OutputRelativePath);
            foreach (var additionalOutput in page.AdditionalOutputs)
            {
                paths.Add(additionalOutput);
            }
        }

        foreach (var staticFile in manifest.StaticFiles)
        {
            paths.Add(staticFile.RelativePath);
        }

        foreach (var artifact in manifest.Artifacts)
        {
            paths.Add(artifact);
        }

        return paths;
    }

    private static void PruneEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            PruneEmptyDirectories(directory);
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private string ComputeOptionsHash()
    {
        var builder = new StringBuilder();
        foreach (var value in new[] { site.BaseUrl.AbsoluteUri, site.Name, site.Description,
            site.Language, site.Author, ToDependencyKey(options.ContentDirectory),
            ToDependencyKey(options.StaticDirectory), ToDependencyKey(options.OutputDirectory) })
        {
            BuildFingerprint.AppendPart(builder, value);
        }

        foreach (var path in buildInputPaths)
        {
            BuildFingerprint.AppendPart(builder, $"path:{path}");
            BuildFingerprint.AppendPart(builder, HashBuildInputPath(path));
        }

        foreach (var (key, value) in buildInputValues)
        {
            BuildFingerprint.AppendPart(builder, key);
            BuildFingerprint.AppendPart(builder, value);
        }

        return BuildFingerprint.HashText(builder.ToString());
    }

    private string HashBuildInputPath(string path)
    {
        var fullPath = Path.IsPathFullyQualified(path) ? path : Path.Combine(_rootPath, path);
        if (Directory.Exists(fullPath))
        {
            var files = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                .Select(file => (Path.GetRelativePath(fullPath, file), BuildFingerprint.HashFile(file)));
            return BuildFingerprint.HashFileSet(files);
        }

        return BuildFingerprint.HashFile(fullPath);
    }

    internal static IReadOnlyList<string> CollectAssemblyMvids(IEnumerable<Assembly> roots)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var mvids = new List<string>();
        var queue = new Queue<Assembly>(roots.Distinct());

        while (queue.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (!visited.Add(name) || IsFrameworkAssembly(name))
            {
                continue;
            }

            mvids.Add($"{name}:{assembly.ManifestModule.ModuleVersionId:N}");

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (visited.Contains(reference.Name ?? string.Empty) || IsFrameworkAssembly(reference.Name ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // Unresolvable references still participate deterministically.
                    visited.Add(reference.Name ?? string.Empty);
                    mvids.Add($"{reference.Name}:unresolved");
                }
            }
        }

        mvids.Sort(StringComparer.Ordinal);
        return mvids;
    }

    private static bool IsFrameworkAssembly(string name)
    {
        // Framework assemblies change only with SDK updates; excluding them keeps the
        // fingerprint small. Use -p:KijiForce=true after an SDK update if in doubt.
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name is "System" or "mscorlib" or "netstandard" or "WindowsBase";
    }

    /// <param name="stamp">
    /// The file's size and last write time when the caller already has them, so the
    /// registry can validate its recorded hash without a filesystem round trip.
    /// </param>
    internal string HashFileCached(string path, (long Length, DateTime LastWriteTimeUtc)? stamp = null)
    {
        // Content files parsed during materialization already carry a stamp-validated
        // hash in the registry; only files nobody read yet are hashed from disk.
        return _fileFingerprints.GetOrAdd(
            Path.GetFullPath(path),
            static (fullPath, state) => new Lazy<string>(() =>
            {
                state.Self.BeforeFileHash?.Invoke(fullPath);
                return state.Registry?.GetValidatedHash(fullPath, state.Stamp) ?? BuildFingerprint.HashFile(fullPath);
            },
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Registry: hashRegistry, Stamp: stamp, Self: this)).Value;
    }

    private string ToDependencyKey(string absolutePath)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        return fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(_rootPath, fullPath).Replace('\\', '/')
            : fullPath;
    }

    private string ResolveDependencyPath(string key)
    {
        return Path.IsPathFullyQualified(key)
            ? key
            : Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
