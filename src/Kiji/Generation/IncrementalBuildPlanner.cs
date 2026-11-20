using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kiji.Rendering;
using Kiji.Hosting;
using Kiji.Assets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    string environmentName,
    IConfiguration configuration,
    ContentFileRegistry? hashRegistry = null,
    DependencyCatalog? catalog = null,
    IImageProcessor? imageProcessor = null,
    AssetResources? assets = null,
    ILogger? logger = null,
    Func<object, ContentItemIdentity?>? resolveContentItem = null,
    ContentPath? contentPaths = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, Lazy<List<string>>> CodeDependencies = [];
    private readonly ArtifactCache _cache = new(cacheDirectory);
    private readonly string _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    private readonly string _manifestPath = Path.Combine(cacheDirectory, "manifest.json");
    private readonly string _outputDirectoryHash = BuildFingerprint.HashText(Path.GetFullPath(options.OutputDirectory));
    private bool _stampsChanged;
    internal bool OutputStampsValid { get; private set; }

    /// <summary>
    /// The output directory did not exist when planning began, so everything in it comes from
    /// this build: nothing there needs comparing, and nothing unknown needs removing.
    /// </summary>
    internal bool OutputDirectoryCreated { get; private set; }
    private readonly ConcurrentDictionary<(string Path, string Hash), Lazy<bool>> _restoredImages = new();
    private readonly ConcurrentDictionary<string, Lazy<BuildManifestOutput>> _imageOutputs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<string>> _imageSources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<string>> _fileFingerprints = new(StringComparer.Ordinal);

    internal async Task<IncrementalBuildPlan> CreatePlanAsync(
        IReadOnlyList<PageRenderRequest> pages,
        IEnumerable<Assembly> assemblies,
        CancellationToken cancellationToken,
        Task<BuildManifest?>? previousManifest = null)
    {
        OutputDirectoryCreated = !Directory.Exists(options.OutputDirectory);
        var optionsHash = ComputeOptionsHash();
        var codeDependencies = CollectCodeDependencies(assemblies);

        var oldManifest = previousManifest is null ? LoadManifest(cacheDirectory, cancellationToken) : await previousManifest;
        OutputStampsValid = oldManifest?.OutputDirectoryHash == _outputDirectoryHash;
        if (OutputDirectoryCreated && oldManifest is not null)
        {
            foreach (var page in oldManifest.Pages)
            {
                page.Stamp = null;
                foreach (var output in page.AdditionalOutputs) { output.Stamp = null; }
            }
        }

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
        var parametersHash = BuildFingerprint.HashParameters(request.Parameters, resolveContentItem);
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

                case "assets":
                    if (dependency.Fingerprint != (assets ?? AssetResources.Empty).Fingerprint) { return false; }
                    break;

                default:
                    return false;
            }
        }

        foreach (var image in oldPage.ImageRequests)
        {
            try
            {
                _ = ResolveImageSource(image.Source);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                var markdown = string.Join(", ", oldPage.Dependencies.Where(dependency => dependency.Kind == BuildManifestDependency.FileKind
                    && dependency.Key.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Select(dependency => dependency.Key));
                throw new InvalidOperationException($"Image source '{image.Source}' used by page '{request.RoutePath}' (Markdown: {markdown}) cannot be resolved. {exception.Message}", exception);
            }
        }
        if (!oldPage.AdditionalOutputs.All(RestoreImageOutput))
        {
            if (imageProcessor is null) { return false; }
            foreach (var image in oldPage.ImageRequests)
            {
                await ImageArtifactProcessor.ProcessAsync(imageProcessor, ResolveImageSource(image.Source),
                    Path.Combine(options.OutputDirectory, image.OutputDirectory), options.ImageCacheDirectory, Path.Combine(cacheDirectory, "work"), cancellationToken);
            }
            if (!oldPage.AdditionalOutputs.All(output =>
                HashFileCached(Path.Combine(options.OutputDirectory, output.RelativePath)) == output.Hash))
            {
                return false;
            }
        }
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

    private string ResolveImageSource(string source) => _imageSources.GetOrAdd(source, source => new Lazy<string>(() =>
        contentPaths?.ResolveFile(ResolveDependencyPath(source)) ?? ResolveDependencyPath(source))).Value;

    private bool RestoreImageOutput(BuildManifestOutput output)
    {
        var restored = _restoredImages.GetOrAdd((output.RelativePath, output.Hash), _ => new Lazy<bool>(() =>
        {
            var path = Path.Combine(options.OutputDirectory, output.RelativePath);
            return (OutputStampsValid && output.Stamp is { } stamp && OutputStamp.Read(path) == stamp)
                || _cache.Restore(output.Hash, path);
        })).Value;
        if (!restored) { _restoredImages.TryRemove((output.RelativePath, output.Hash), out _); }
        return restored;
    }

    internal BuildManifestPage CreatePageEntry(
        PageRenderRequest request,
        string outputHash,
        BuildDependencyRecorder recorder,
        byte[] html,
        OutputStamp? stamp = null)
    {
        var dependencies = new List<BuildManifestDependency>();

        foreach (var (file, recorded) in recorder.FileInputs.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            var key = ContentPath.GetRelativePath(_rootPath, file);
            if (key is null) { recorder.DisableCache(); }
            dependencies.Add(new BuildManifestDependency(
                BuildManifestDependency.FileKind,
                key ?? "untracked-external-file",
                recorded));
        }

        dependencies.AddRange(recorder.Values);
        if (recorder.AssetsFingerprint is { } assetsFingerprint)
        {
            dependencies.Add(new BuildManifestDependency("assets", "", assetsFingerprint));
        }
        var additionalOutputs = recorder.AdditionalOutputs.Select(path =>
            _imageOutputs.GetOrAdd(path, path => new Lazy<BuildManifestOutput>(() => new BuildManifestOutput(
                Path.GetRelativePath(options.OutputDirectory, path).Replace(Path.DirectorySeparatorChar, '/'),
                _cache.Store(path)))).Value).ToArray();
        var parametersHash = recorder.Cacheable ? BuildFingerprint.HashParameters(request.Parameters, resolveContentItem) : null;
        return new BuildManifestPage(
            request.OutputRelativePath,
            request.RoutePath,
            parametersHash,
            outputHash,
            dependencies,
            additionalOutputs)
        {
            Html = html,
            Stamp = stamp,
            Images = recorder.Images,
            ImageRequests = parametersHash is null ? [] : [.. recorder.ImageRequests.Select(image => new BuildManifestImage(
                ToDependencyKey(image.Source), Path.GetRelativePath(options.OutputDirectory, image.OutputDirectory).Replace(Path.DirectorySeparatorChar, '/')))],
        };
    }

    /// <summary>
    /// Copies static files whose source and destination content hashes differ.
    /// </summary>
    internal async Task<IReadOnlyList<string>> SyncStaticFilesAsync(IReadOnlyList<StaticAsset> files)
    {
        var entries = new string[files.Count];
        var copied = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            (index, _) =>
            {
                var source = files[index];
                var relativePath = source.Target.Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = OutputPathValidator.ResolveUnderRoot(options.OutputDirectory, relativePath, "Static asset path");
                entries[index] = relativePath;

                if (!File.Exists(source.Source))
                {
                    throw new FileNotFoundException("A static asset is missing. Rebuild the site to refresh its asset manifest.", source.Source);
                }

                var outputHash = BuildFingerprint.HashFile(destinationPath);
                var inputHash = source.ExpectedHash is not null || outputHash != BuildFingerprint.Missing
                    ? BuildFingerprint.HashFile(source.Source) : null;
                if (source.ExpectedHash is not null && source.ExpectedHash != inputHash)
                {
                    throw new IOException($"Static asset '{source.Source}' changed after the SDK computed its URL. Rebuild the site before publishing.");
                }
                if (outputHash != BuildFingerprint.Missing && outputHash == inputHash)
                {
                    return ValueTask.CompletedTask;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(source.Source, destinationPath, overwrite: true);
                if (source.ExpectedHash is not null && BuildFingerprint.HashFile(destinationPath) != source.ExpectedHash)
                {
                    throw new IOException($"Static asset '{source.Source}' changed while being copied. Rebuild the site before publishing.");
                }
                File.SetLastWriteTimeUtc(destinationPath, DateTime.UtcNow);
                Interlocked.Increment(ref copied);

                return ValueTask.CompletedTask;
            });

        if (files.Count > 0)
        {
            _logger.StaticFilesSynced(copied, files.Count - copied);
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

        if (OutputDirectoryCreated)
        {
            _stampsChanged = true;
            var imageStamps = new Dictionary<string, OutputStamp?>(StringComparer.OrdinalIgnoreCase);
            foreach (var page in manifest.Pages)
            {
                page.Stamp ??= OutputStamp.Read(Path.Combine(options.OutputDirectory, page.OutputRelativePath));
                foreach (var output in page.AdditionalOutputs)
                {
                    if (!imageStamps.TryGetValue(output.RelativePath, out var imageStamp))
                    {
                        imageStamp = OutputStamp.Read(Path.Combine(options.OutputDirectory, output.RelativePath));
                        imageStamps.Add(output.RelativePath, imageStamp);
                    }
                    output.Stamp = imageStamp;
                }
            }
            return;
        }

        var expected = CollectOutputRelativePaths(manifest);
        var images = manifest.Pages.SelectMany(page => page.AdditionalOutputs)
            .ToLookup(output => output.RelativePath, StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        DirectoryInfo[] directories = [new(options.OutputDirectory)];
        while (directories.Length > 0)
        {
            var children = new ConcurrentBag<DirectoryInfo>();
            Parallel.ForEach(directories, directory =>
            {
                foreach (var entry in directory.EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo child) { children.Add(child); continue; }
                    var file = (FileInfo)entry;
                    var relativePath = Path.GetRelativePath(options.OutputDirectory, file.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    if (expected.TryGetValue(relativePath, out var page))
                    {
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
            _logger.OutputsRemoved(removed);
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
        if (previous is null || manifest.OptionsHash != previous.OptionsHash
            || !manifest.CodeDependencies.SequenceEqual(previous.CodeDependencies)
            || !manifest.Pages.SequenceEqual(previous.Pages)
            || !manifest.StaticFiles.SequenceEqual(previous.StaticFiles)
            || !manifest.Artifacts.SequenceEqual(previous.Artifacts))
        {
            var htmlFile = "html-" + Guid.NewGuid().ToString("N") + ".bin";
            var htmlPath = Path.Combine(cacheDirectory, htmlFile);
            try
            {
                using (var stream = new FileStream(htmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
                {
                    foreach (var page in manifest.Pages)
                    {
                        page.HtmlOffset = checked((int)stream.Position);
                        if (page.ParametersHash is null) { page.HtmlLength = 0; continue; }
                        var html = page.Html ?? throw new InvalidDataException("Missing rendered HTML.");
                        page.HtmlLength = html.Length;
                        stream.Write(html.Span);
                    }
                }
                manifest = manifest with { HtmlFile = htmlFile };
                WriteManifest(manifest);
            }
            catch { File.Delete(htmlPath); throw; }
        }
        else
        {
            manifest = manifest with { HtmlFile = previous.HtmlFile };
            if (_stampsChanged || !OutputStampsValid)
            {
                WriteManifest(manifest);
            }
        }
        Collect(manifest);
    }

    private void WriteManifest(BuildManifest manifest)
    {
        var json = new System.Buffers.ArrayBufferWriter<byte>(Math.Max(64 * 1024, manifest.Pages.Count * 1024));
        BuildManifestSerializer.Serialize(json, manifest);
        ArtifactCache.WriteAtomic(_manifestPath, json.WrittenSpan);
    }

    private void Collect(BuildManifest manifest)
    {
        File.Delete(Path.Combine(cacheDirectory, "build-manifest.json"));
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

    /// <summary>
    /// Reads and validates the previous build's manifest. It depends only on the cache, so a
    /// publish reads it, under the cache lease, while its content loads.
    /// </summary>
    internal static BuildManifest? LoadManifest(string cacheDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = BuildManifestSerializer.Deserialize(File.ReadAllBytes(manifestPath));
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

    internal static void VerifyInputs(IEnumerable<BuildDependencyRecorder> recorders)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
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
        var files = new string[inputs.Count];
        inputs.Keys.CopyTo(files, 0);
        Parallel.For(0, files.Length, index =>
        {
            var expected = inputs[files[index]];
            if (expected != BuildFingerprint.Missing && BuildFingerprint.HashFile(files[index]) != expected)
            {
                throw new IOException("An input changed during the build: " + files[index]);
            }
        });
    }

    private static Dictionary<string, BuildManifestPage?> CollectOutputRelativePaths(BuildManifest manifest)
    {
        var paths = new Dictionary<string, BuildManifestPage?>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in manifest.Pages)
        {
            paths.Add(page.OutputRelativePath.Replace(Path.DirectorySeparatorChar, '/'), page);
            foreach (var additionalOutput in page.AdditionalOutputs)
            {
                paths.TryAdd(additionalOutput.RelativePath.Replace(Path.DirectorySeparatorChar, '/'), null);
            }
        }

        foreach (var staticFile in manifest.StaticFiles)
        {
            paths.TryAdd(staticFile.Replace(Path.DirectorySeparatorChar, '/'), null);
        }

        foreach (var artifact in manifest.Artifacts)
        {
            paths.TryAdd(artifact.Replace(Path.DirectorySeparatorChar, '/'), null);
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
            site.Language, site.Author, environmentName })
        {
            BuildFingerprint.AppendPart(builder, value);
        }

        foreach (var (key, value) in configuration.AsEnumerable()
            .Where(static pair => !IsDiagnosticsKey(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            BuildFingerprint.AppendPart(builder, key);
            BuildFingerprint.AppendPart(builder, value);
        }

        return BuildFingerprint.HashText(builder.ToString());
    }

    private static bool IsDiagnosticsKey(string key) => IsSection(key, "Logging") || IsSection(key, "Metrics");

    private static bool IsSection(string key, string section) =>
        key.StartsWith(section, StringComparison.OrdinalIgnoreCase)
        && (key.Length == section.Length || key[section.Length] == ConfigurationPath.KeyDelimiter[0]);

    internal static IReadOnlyList<string> CollectCodeDependencies(IEnumerable<Assembly> roots)
    {
        return [.. roots.Distinct().SelectMany(root => CodeDependencies.GetValue(root,
                static root => new Lazy<List<string>>(() => CollectAssemblyDependencies(root))).Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static List<string> CollectAssemblyDependencies(Assembly root)
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
        return ContentPath.GetRelativePath(_rootPath, fullPath) ?? fullPath;
    }

    private string ResolveDependencyPath(string key)
    {
        return Path.IsPathFullyQualified(key)
            ? key
            : Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
