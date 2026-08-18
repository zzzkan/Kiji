using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiji.Rendering;

namespace Kiji.Generation;

/// <summary>
/// Drives the incremental build: loads the previous manifest, fingerprints the
/// current inputs (options, assemblies, content files), decides per page whether the
/// existing output is still valid, syncs static files by stamp, removes orphaned
/// outputs, and writes the new manifest. Every ambiguous situation falls back to a
/// full rebuild — a stale output is never acceptable.
/// </summary>
internal sealed class IncrementalBuildPlanner(
    SsgOptions options,
    string rootPath,
    string cacheDirectory,
    SiteInfo site,
    IReadOnlyList<KijiBuildInput> buildInputs,
    ContentFileHashRegistry? hashRegistry = null)
{
    private readonly string _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    private readonly string _manifestPath = Path.Combine(cacheDirectory, "build-manifest.json");
    private readonly ConcurrentDictionary<string, string> _fileFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _contentSetFingerprints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The scope key written by builds predating per-directory content sets.</summary>
    private const string LegacyWholeTreeScope = "contents";

    internal async Task<IncrementalBuildPlan> CreatePlanAsync(
        IReadOnlyList<PageRenderRequest> pages,
        IEnumerable<Assembly> assemblies,
        bool force,
        CancellationToken cancellationToken)
    {
        var optionsHash = ComputeOptionsHash();
        var assemblyMvids = CollectAssemblyMvids(assemblies);

        var oldManifest = force ? null : await LoadManifestAsync(cancellationToken);

        // No usable manifest means the output directory's contents are unknown:
        // rebuild from a clean slate. Same when files appeared that no build produced.
        var fullClean = oldManifest is null;
        if (!fullClean && HasUnknownOutputs(oldManifest!))
        {
            fullClean = true;
            oldManifest = null;
        }

        var renderAll = fullClean
            || oldManifest!.OptionsHash != optionsHash
            || !oldManifest.AssemblyMvids.SequenceEqual(assemblyMvids, StringComparer.Ordinal);

        if (renderAll)
        {
            return new IncrementalBuildPlan(fullClean, pages, [], oldManifest, optionsHash, assemblyMvids);
        }

        var oldPages = oldManifest!.Pages.ToDictionary(
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

        return new IncrementalBuildPlan(
            FullClean: false, pagesToRender, carriedPages, oldManifest, optionsHash, assemblyMvids);
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
        var outputPath = Path.Combine(options.OutputPath, oldPage.OutputRelativePath);
        if (!StampMatches(outputPath, oldPage.OutputLength, oldPage.OutputLastWriteTimeUtc)
            && !string.Equals(HashFileCached(outputPath), oldPage.OutputHash, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var additionalOutput in oldPage.AdditionalOutputs)
        {
            if (!File.Exists(Path.Combine(options.OutputPath, additionalOutput)))
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
        var dependencies = new List<BuildManifestDependency>();
        foreach (var file in recorder.Files.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var stamp = ReadStamp(file);
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.FileKind,
                ToDependencyKey(file),
                HashFileCached(file),
                stamp?.Length,
                stamp?.LastWriteTimeUtc));
        }

        foreach (var scope in recorder.ContentSetScopes.OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase))
        {
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.ContentSetKind,
                scope,
                ContentSetFingerprint(scope)));
        }

        var additionalOutputs = recorder.AdditionalOutputs
            .Select(path => Path.GetRelativePath(options.OutputPath, path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var outputStamp = ReadStamp(Path.Combine(options.OutputPath, request.OutputRelativePath));
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
        if (!Directory.Exists(options.StaticPath))
        {
            return [];
        }

        var oldEntries = (plan.OldManifest?.StaticFiles ?? [])
            .ToDictionary(static entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(options.StaticPath, "*", SearchOption.AllDirectories).ToArray();
        var entries = new BuildManifestStaticFile[files.Length];
        var copied = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            (index, _) =>
            {
                var source = new FileInfo(files[index]);
                var relativePath = Path.GetRelativePath(options.StaticPath, source.FullName);
                var destinationPath = Path.Combine(options.OutputPath, relativePath);

                if (!plan.FullClean
                    && oldEntries.TryGetValue(relativePath, out var oldEntry)
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
    /// Deletes files the previous build produced that no current output claims, then
    /// prunes directories left empty.
    /// </summary>
    internal void RemoveOrphans(BuildManifest? oldManifest, BuildManifest newManifest)
    {
        if (oldManifest is null)
        {
            return;
        }

        var expected = CollectOutputRelativePaths(newManifest);
        var removed = 0;

        foreach (var oldOutput in CollectOutputRelativePaths(oldManifest))
        {
            if (expected.Contains(oldOutput))
            {
                continue;
            }

            var fullPath = Path.Combine(options.OutputPath, oldOutput);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                removed++;
            }
        }

        if (removed > 0)
        {
            PruneEmptyDirectories(options.OutputPath);
            BuildOutput.Info($"Removed {removed} stale output file(s).");
        }
    }

    internal async Task SaveManifestAsync(BuildManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        var json = JsonSerializer.Serialize(manifest, BuildManifestJsonContext.Default.BuildManifest);
        await File.WriteAllTextAsync(_manifestPath, json, cancellationToken);
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
            return manifest?.SchemaVersion == BuildManifest.CurrentSchemaVersion ? manifest : null;
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
    /// <remarks>
    /// <c>"contents"</c> is how builds before scoping keyed the whole-tree dependency,
    /// so it is still read that way. A site that really does have a
    /// <c>contents/contents/</c> directory therefore gets the whole-tree digest for it:
    /// a superset, so the page can only re-render more often, never go stale.
    /// </remarks>
    internal string ContentSetFingerprint(string scope)
    {
        return _contentSetFingerprints.GetOrAdd(scope, static (key, self) => self.ComputeContentSetFingerprint(key), this);
    }

    internal string ComputeContentSetFingerprint(string scope = "")
    {
        var scopePath = scope.Length == 0 || string.Equals(scope, LegacyWholeTreeScope, StringComparison.OrdinalIgnoreCase)
            ? options.ContentsPath
            : Path.GetFullPath(Path.Combine(options.ContentsPath, scope));

        if (!Directory.Exists(scopePath))
        {
            return BuildFingerprint.Missing;
        }

        var files = Directory.EnumerateFiles(scopePath, "*.md", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();

        var hashed = new (string RelativePath, string ContentHash)[files.Length];
        Parallel.For(0, files.Length, index =>
        {
            hashed[index] = (
                Path.GetRelativePath(options.ContentsPath, files[index]!),
                HashFileCached(files[index]!));
        });

        return BuildFingerprint.HashFileSet(hashed);
    }

    private bool HasUnknownOutputs(BuildManifest oldManifest)
    {
        if (!Directory.Exists(options.OutputPath))
        {
            return false;
        }

        var expected = CollectOutputRelativePaths(oldManifest);
        foreach (var file in Directory.EnumerateFiles(options.OutputPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(options.OutputPath, file);
            if (!expected.Contains(relativePath))
            {
                BuildOutput.Info($"Unexpected file in output directory ('{relativePath}'); rebuilding from scratch.");
                return true;
            }
        }

        return false;
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
        var builder = new StringBuilder()
            .Append("baseUrl=").Append(site.BaseUrl).Append('\n')
            .Append("name=").Append(site.Name).Append('\n')
            .Append("description=").Append(site.Description).Append('\n')
            .Append("language=").Append(site.Language).Append('\n')
            .Append("author=").Append(site.Author).Append('\n')
            .Append("contents=").Append(ToDependencyKey(options.ContentsPath)).Append('\n')
            .Append("static=").Append(ToDependencyKey(options.StaticPath)).Append('\n')
            .Append("output=").Append(ToDependencyKey(options.OutputPath)).Append('\n');

        foreach (var input in buildInputs)
        {
            builder.Append("input:").Append(input.Key).Append('=');
            builder.Append(input.Path is not null ? HashBuildInputPath(input.Path) : input.Value);
            builder.Append('\n');
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
        // fingerprint small. Use --force after an SDK update if in doubt.
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name is "System" or "mscorlib" or "netstandard" or "WindowsBase";
    }

    internal string HashFileCached(string path)
    {
        // Content files parsed during materialization already carry a stamp-validated
        // hash in the registry; only files nobody read yet are hashed from disk.
        return _fileFingerprints.GetOrAdd(
            Path.GetFullPath(path),
            fullPath => hashRegistry?.GetValidatedHash(fullPath) ?? BuildFingerprint.HashFile(fullPath));
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
