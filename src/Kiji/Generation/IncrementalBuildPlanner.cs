using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiji.Rendering;
using Kiji.Assets;

namespace Kiji.Generation;

/// <summary>
/// Drives the incremental build: loads the previous manifest, fingerprints the
/// current inputs (options, assemblies, content files), decides per page whether the
/// existing output is still valid, syncs static files by content hash, reconciles the output
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
    ContentFileRegistry? hashRegistry = null,
    DependencyCatalog? catalog = null,
    IImageProcessor? imageProcessor = null)
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, IReadOnlyList<string>> CodeDependencies = [];
    private readonly ArtifactCache _cache = new(cacheDirectory);
    private readonly string _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    private readonly string _manifestPath = Path.Combine(cacheDirectory, "manifest.json");
    private readonly string _outputDirectoryHash = BuildFingerprint.HashText(Path.GetFullPath(options.OutputDirectory));
    private bool _stampsChanged;
    internal bool OutputStampsValid { get; private set; }
    private readonly ConcurrentDictionary<string, Lazy<string>> _fileFingerprints = new(StringComparer.OrdinalIgnoreCase);

    internal async Task<IncrementalBuildPlan> CreatePlanAsync(
        IReadOnlyList<PageRenderRequest> pages,
        IEnumerable<Assembly> assemblies,
        bool force,
        CancellationToken cancellationToken)
    {
        var optionsHash = ComputeOptionsHash();
        var codeDependencies = CollectCodeDependencies(assemblies);

        var oldManifest = force ? null : LoadManifest(cancellationToken);
        OutputStampsValid = oldManifest?.OutputDirectoryHash == _outputDirectoryHash;

        // Without a manifest nothing about the previous build can be proven, so every
        // page renders. Unrecognized files in the output directory are not a reason to
        // re-render anything: the reconciliation pass at the end of the build deletes
        // whatever this build did not produce.
        if (oldManifest is null
            || codeDependencies.Any(static input => input.EndsWith(":unavailable", StringComparison.Ordinal))
            || oldManifest.OptionsHash != optionsHash
            || !oldManifest.CodeDependencies.SequenceEqual(codeDependencies, StringComparer.Ordinal))
        {
            return new IncrementalBuildPlan(pages, [], oldManifest, optionsHash, codeDependencies);
        }

        var oldPages = oldManifest.Pages.ToDictionary(
            static page => page.OutputRelativePath,
            StringComparer.OrdinalIgnoreCase);

        // Code changes need only the old output hashes. Load HTML only after the
        // global code/options check has established that pages may be reused.
        LoadHtml(oldManifest);

        var decisions = new (PageRenderRequest? Render, BuildManifestPage? Carried)[pages.Count];
        var outputDirectoryExists = Directory.Exists(options.OutputDirectory);
        await Parallel.ForEachAsync(Enumerable.Range(0, pages.Count), cancellationToken, async (index, ct) =>
        {
            var request = pages[index];
            decisions[index] = oldPages.TryGetValue(request.OutputRelativePath, out var oldPage)
                && await CanSkipAsync(request, oldPage, outputDirectoryExists, ct)
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

        return new IncrementalBuildPlan(pagesToRender, carriedPages, oldManifest, optionsHash, codeDependencies);
    }

    private async Task<bool> CanSkipAsync(PageRenderRequest request, BuildManifestPage oldPage, bool outputDirectoryExists, CancellationToken cancellationToken)
    {
        var parametersHash = BuildFingerprint.HashParameters(request.Parameters);
        if (parametersHash is null || oldPage.ParametersHash is null
            || !string.Equals(oldPage.RoutePath, request.RoutePath, StringComparison.Ordinal)
            || !string.Equals(oldPage.ParametersHash, parametersHash, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var dependency in oldPage.Dependencies)
        {
            switch (dependency.Kind)
            {
                case BuildManifestDependency.FileKind:
                    var dependencyPath = ResolveDependencyPath(dependency.Key);
                    if (!string.Equals(HashFileCached(dependencyPath), dependency.Fingerprint, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    break;

                case "value":
                    if (catalog?.Resolve(dependency.Key) is not { } value || BuildFingerprint.HashText(value) != dependency.Fingerprint) { return false; }
                    break;

                default:
                    return false; // Unknown kind from a newer schema: re-render.
            }
        }

        if (!oldPage.AdditionalOutputs.All(RestoreImageOutput))
        {
            if (imageProcessor is null) { return false; }
            foreach (var image in oldPage.ImageRequests)
            {
                await ImageArtifactProcessor.ProcessAsync(imageProcessor, ResolveDependencyPath(image.Source),
                    Path.Combine(options.OutputDirectory, image.OutputDirectory), options.ImageCacheDirectory, cancellationToken);
            }
            if (!oldPage.AdditionalOutputs.All(output =>
                BuildFingerprint.HashFile(Path.Combine(options.OutputDirectory, output.RelativePath)) == output.Hash))
            {
                return false;
            }
        }
        // Verify cached bytes even when output remains: successful publication must
        // leave a usable cache for the next clean checkout as well.
        if (oldPage.Html is not { } html || BuildFingerprint.HashBytes(html.Span) != oldPage.OutputHash)
        {
            return false;
        }
        var outputPath = Path.Combine(options.OutputDirectory, oldPage.OutputRelativePath);
        var stamp = outputDirectoryExists ? OutputStamp.Read(outputPath) : null;
        if (!(OutputStampsValid && stamp is not null && stamp == oldPage.Stamp)
            && (!outputDirectoryExists || !BuildFingerprint.FileEquals(outputPath, html.Span)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, html.Span);
        }
        return true;
    }

    private bool RestoreImageOutput(BuildManifestOutput output)
    {
        var path = Path.Combine(options.OutputDirectory, output.RelativePath);
        return (OutputStampsValid && output.Stamp is { } stamp && OutputStamp.Read(path) == stamp)
            || _cache.Restore(output.Hash, path);
    }

    internal BuildManifestPage CreatePageEntry(
        PageRenderRequest request,
        string outputHash,
        BuildDependencyRecorder recorder,
        byte[] html)
    {
        // A deterministic dependency order makes cache records reproducible.
        var dependencies = new List<BuildManifestDependency>();

        foreach (var (file, recorded) in recorder.FileInputs.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = ToDependencyKey(file);
            if (!BuildManifest.IsRelativeOutput(key)) { recorder.DisableCache(); }
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.FileKind,
                BuildManifest.IsRelativeOutput(key) ? key : "untracked-external-file",
                recorded));
        }

        dependencies.AddRange(recorder.Values);
        var additionalOutputs = recorder.AdditionalOutputs.Select(path => new BuildManifestOutput(
            Path.GetRelativePath(options.OutputDirectory, path),
            _cache.Store(path))).ToArray();
        var parametersHash = recorder.Cacheable ? BuildFingerprint.HashParameters(request.Parameters) : null;
        return new BuildManifestPage(
            request.OutputRelativePath,
            request.RoutePath,
            parametersHash,
            outputHash,
            dependencies,
            additionalOutputs)
        {
            Html = html,
            Images = recorder.Images,
            // Pages that always render need no repair recipes. In particular,
            // external sources have no portable, root-relative identity.
            ImageRequests = parametersHash is null ? [] : [.. recorder.ImageRequests.Select(image => new BuildManifestImage(
                ToDependencyKey(image.Source), Path.GetRelativePath(options.OutputDirectory, image.OutputDirectory)))],
        };
    }

    /// <summary>
    /// Copies static files whose source and destination content hashes differ.
    /// </summary>
    internal async Task<IReadOnlyList<string>> SyncStaticFilesAsync()
    {
        if (!Directory.Exists(options.StaticDirectory))
        {
            return [];
        }

        var files = new DirectoryInfo(options.StaticDirectory).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
        var entries = new string[files.Length];
        var copied = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            (index, _) =>
            {
                var source = files[index];
                var relativePath = Path.GetRelativePath(options.StaticDirectory, source.FullName);
                var destinationPath = Path.Combine(options.OutputDirectory, relativePath);
                entries[index] = relativePath;

                if (BuildFingerprint.HashFile(source.FullName) == BuildFingerprint.HashFile(destinationPath))
                {
                    return ValueTask.CompletedTask;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(source.FullName, destinationPath, overwrite: true);
                Interlocked.Increment(ref copied);

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
        var images = manifest.Pages.SelectMany(page => page.AdditionalOutputs)
            .ToLookup(output => output.RelativePath, StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        DirectoryInfo[] directories = [new(options.OutputDirectory)];
        while (directories.Length > 0)
        {
            // Scan independent directories together. A recursive enumerator opens
            // every page bundle serially even when no output needs changing.
            var children = new ConcurrentBag<DirectoryInfo>();
            Parallel.ForEach(directories, directory =>
            {
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo child) { children.Add(child); continue; }
                    var file = (FileInfo)entry;
                    var relativePath = Path.GetRelativePath(options.OutputDirectory, file.FullName);
                    if (expected.TryGetValue(relativePath, out var page))
                    {
                        // Enumeration already supplies size and time; no second stat.
                        var stamp = new OutputStamp(file.Length, file.LastWriteTimeUtc);
                        if (page is not null)
                        {
                            if (page.Stamp != stamp) { _stampsChanged = true; page.Stamp = stamp; }
                        }
                        else
                        {
                            foreach (var image in images[relativePath])
                            {
                                if (image.Stamp != stamp) { _stampsChanged = true; image.Stamp = stamp; }
                            }
                        }
                        continue;
                    }

                    file.Delete();
                    Interlocked.Increment(ref removed);
                }
            });
            directories = [.. children];
        }

        if (removed > 0)
        {
            PruneEmptyDirectories(options.OutputDirectory);
            BuildOutput.Info($"Removed {removed} file(s) the build did not produce.");
        }
    }

    internal void SaveManifest(BuildManifest manifest, BuildManifest? previous, CancellationToken cancellationToken)
    {
        manifest = manifest with { OutputDirectoryHash = _outputDirectoryHash };
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);

        cancellationToken.ThrowIfCancellationRequested();
        if (!manifest.IsValid(requireHtmlFile: false)) { throw new InvalidDataException("Invalid or conflicting build outputs."); }
        foreach (var page in manifest.Pages)
        {
            foreach (var output in page.AdditionalOutputs)
            {
                _cache.EnsureStored(output.Hash, Path.Combine(options.OutputDirectory, output.RelativePath));
            }
        }
        // Carried entries are the same objects loaded from the verified manifest.
        // Restoring output changes only its stamps; retain the HTML bundle on
        // no-change builds, including a clean CI checkout.
        if (previous is null || manifest.OptionsHash != previous.OptionsHash
            || !manifest.CodeDependencies.SequenceEqual(previous.CodeDependencies)
            || !manifest.Pages.SequenceEqual(previous.Pages)
            || !manifest.StaticFiles.SequenceEqual(previous.StaticFiles)
            || !manifest.Artifacts.SequenceEqual(previous.Artifacts))
        {
            // The manifest publishes a complete immutable bundle. Until its atomic
            // replacement succeeds, the previous bundle remains usable.
            var htmlFile = "html-" + Guid.NewGuid().ToString("N") + ".bin";
            var htmlPath = Path.Combine(cacheDirectory, htmlFile);
            try
            {
                using (var stream = new FileStream(htmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
                {
                    foreach (var page in manifest.Pages)
                    {
                        page.HtmlOffset = checked((int)stream.Position);
                        // An explicitly uncacheable page must render on every build.
                        // Keep its output metadata for reconciliation, but not its HTML.
                        if (page.ParametersHash is null) { page.HtmlLength = 0; continue; }
                        var html = page.Html ?? throw new InvalidDataException("Missing rendered HTML.");
                        page.HtmlLength = html.Length;
                        stream.Write(html.Span);
                    }
                }
                manifest = manifest with { HtmlFile = htmlFile };
                ArtifactCache.WriteAtomic(_manifestPath,
                    JsonSerializer.SerializeToUtf8Bytes(manifest, BuildManifestJsonContext.Default.BuildManifest));
            }
            catch { File.Delete(htmlPath); throw; }
        }
        else
        {
            manifest = manifest with { HtmlFile = previous.HtmlFile };
            if (_stampsChanged || !OutputStampsValid)
            {
                ArtifactCache.WriteAtomic(_manifestPath,
                    JsonSerializer.SerializeToUtf8Bytes(manifest, BuildManifestJsonContext.Default.BuildManifest));
            }
        }
        Collect(manifest);
    }

    private void Collect(BuildManifest manifest)
    {
        File.Delete(Path.Combine(cacheDirectory, "build-manifest.json")); // Retired legacy manifest.
        foreach (var path in Directory.EnumerateFiles(cacheDirectory, "html-*.bin"))
        {
            if (Path.GetFileName(path) != manifest.HtmlFile) { File.Delete(path); }
        }
        var work = Path.GetFullPath(Path.Combine(cacheDirectory, "work"));
        var cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheDirectory));
        if (work.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }
        var records = Path.Combine(cacheDirectory, "image-records");
        var live = manifest.Pages.SelectMany(p => p.Images).ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(records))
        {
            foreach (var path in Directory.EnumerateFiles(records))
            {
                if (!live.Contains(Path.GetFileNameWithoutExtension(path))) { File.Delete(path); }
            }
        }
        var retiredPages = Path.Combine(cacheRoot, "pages");
        if (Directory.Exists(retiredPages)) { Directory.Delete(retiredPages, recursive: true); }
        _cache.Collect(manifest.Pages.SelectMany(p => p.AdditionalOutputs.Select(o => o.Hash)));
    }

    private BuildManifest? LoadManifest(CancellationToken cancellationToken)
    {
        if (!File.Exists(_manifestPath))
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(_manifestPath), BuildManifestJsonContext.Default.BuildManifest);
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

    private void LoadHtml(BuildManifest manifest)
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(cacheDirectory, manifest.HtmlFile));
            foreach (var page in manifest.Pages)
            {
                if (page.HtmlOffset >= 0 && page.HtmlLength >= 0 && page.HtmlOffset <= bytes.Length
                    && page.HtmlLength <= bytes.Length - page.HtmlOffset)
                {
                    page.Html = bytes.AsMemory(page.HtmlOffset, page.HtmlLength);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    // Validate every consumed file once after rendering. An index page may read
    // thousands of files; parallelizing by file also keeps a one-page edit cheap.
    internal static void VerifyInputs(IEnumerable<BuildDependencyRecorder> recorders)
    {
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recorder in recorders)
        {
            foreach (var (path, digest) in recorder.FileInputs)
            {
                if (inputs.TryGetValue(path, out var previous) && previous != digest)
                {
                    throw new IOException("An input changed during rendering: " + path);
                }
                inputs[path] = digest;
            }
        }
        Parallel.ForEach(inputs, input =>
        {
            if (input.Value != BuildFingerprint.Missing && BuildFingerprint.HashFile(input.Key) != input.Value)
            {
                throw new IOException("An input changed during the build: " + input.Key);
            }
        });
    }

    private static Dictionary<string, BuildManifestPage?> CollectOutputRelativePaths(BuildManifest manifest)
    {
        var paths = new Dictionary<string, BuildManifestPage?>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in manifest.Pages)
        {
            paths.Add(page.OutputRelativePath, page);
            foreach (var additionalOutput in page.AdditionalOutputs)
            {
                paths.TryAdd(additionalOutput.RelativePath, null);
            }
        }

        foreach (var staticFile in manifest.StaticFiles)
        {
            paths.TryAdd(staticFile, null);
        }

        foreach (var artifact in manifest.Artifacts)
        {
            paths.TryAdd(artifact, null);
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
            site.Language, site.Author })
        {
            BuildFingerprint.AppendPart(builder, value);
        }

        foreach (var path in buildInputPaths)
        {
            BuildFingerprint.AppendPart(builder, "file-input");
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

    internal static IReadOnlyList<string> CollectCodeDependencies(IEnumerable<Assembly> roots)
    {
        // A loaded assembly's references and code identity cannot change. Reuse
        // its complete dependency list across publishes, including framework code.
        return [.. roots.Distinct().SelectMany(root => CodeDependencies.GetValue(root, CollectAssemblyDependencies))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> CollectAssemblyDependencies(Assembly root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new List<string>();
        var queue = new Queue<Assembly>([root]);

        while (queue.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (!visited.Add(name))
            {
                continue;
            }

            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                fingerprints.Add($"{name}:unavailable");
                continue;
            }
            // Trust the compiler's module identity. Recompilation belongs to MSBuild;
            // Kiji does not reconstruct compiler inputs or inspect binary contents.
            var mvid = assembly.ManifestModule.ModuleVersionId;
            fingerprints.Add(mvid == Guid.Empty ? $"{name}:unavailable" : $"{name}:mvid:{mvid:N}");

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (visited.Contains(reference.Name ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // An unreadable dependency cannot establish code equivalence.
                    visited.Add(reference.Name ?? string.Empty);
                    fingerprints.Add($"{reference.Name}:unavailable");
                }
            }
        }

        fingerprints.Sort(StringComparer.Ordinal);
        return fingerprints;
    }

    private string HashFileCached(string path)
    {
        // Cache fresh hashes only within this plan, never across build snapshots.
        return _fileFingerprints.GetOrAdd(
            Path.GetFullPath(path),
            static (fullPath, registry) => new Lazy<string>(
                () => registry?.GetSnapshotHash(fullPath) ?? BuildFingerprint.HashFile(fullPath),
                LazyThreadSafetyMode.ExecutionAndPublication),
            hashRegistry).Value;
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
