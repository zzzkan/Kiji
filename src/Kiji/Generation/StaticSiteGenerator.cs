using Kiji.Rendering;

namespace Kiji.Generation;

internal static class StaticSiteGenerator
{
    /// <param name="previousOutputs">
    /// What the last build recorded about each output path, keyed by relative path.
    /// Lets a page whose render produced the bytes already on disk skip the write.
    /// Null when nothing may be assumed about the output directory.
    /// </param>
    internal static async Task<IReadOnlyList<RenderedPage>> RenderPagesAsync(
        ResolvedSitePaths options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        IReadOnlyDictionary<string, BuildManifestPage>? previousOutputs,
        CancellationToken cancellationToken)
    {
        // Resolve every output path once and create the directory set up front, so
        // the parallel render loop issues no per-page directory syscalls.
        var resolvedPages = new (PageRenderRequest Request, string FullPath)[pageRequests.Count];
        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pageRequests.Count; i++)
        {
            var fullPath = ResolvePageOutputPath(options.OutputDirectory, pageRequests[i].OutputRelativePath);
            resolvedPages[i] = (pageRequests[i], fullPath);
            outputDirectories.Add(Path.GetDirectoryName(fullPath)!);
        }

        foreach (var directory in outputDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        var rendered = new RenderedPage[resolvedPages.Length];

        // Each page renders on its own HtmlRenderer/DI scope, so pages are safe to
        // render concurrently.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, resolvedPages.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (index, ct) =>
            {
                var (request, fullPath) = resolvedPages[index];
                var previous = previousOutputs is not null
                    && previousOutputs.TryGetValue(request.OutputRelativePath, out var recorded)
                        ? recorded
                        : null;
                rendered[index] = await WritePageAsync(request, fullPath, renderPageAsync, previous, ct);
            });

        return rendered;
    }

    private static async Task<RenderedPage> WritePageAsync(
        PageRenderRequest pageRequest,
        string fullPath,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        BuildManifestPage? previous,
        CancellationToken cancellationToken)
    {
        // Render into a pooled UTF-8 buffer, then persist with one preallocated write:
        // no StreamWriter/FileStream buffers and no chunked async writes per page.
        using var writer = new PooledUtf8TextWriter();
        await renderPageAsync(pageRequest, writer, cancellationToken);
        var outputHash = writer.GetContentHash();

        // Rendering may produce identical HTML. Skip rewriting only when its hash
        // and the existing output stamp still match the previous build.
        var written = !AlreadyOnDisk(fullPath, previous, outputHash);
        if (written)
        {
            writer.WriteToFile(fullPath);
        }

        BuildOutput.Detail($"{(written ? "Generated" : "Unchanged")}: {fullPath}");
        return new RenderedPage(pageRequest, outputHash, written);
    }

    /// <summary>
    /// Whether the file already holds the bytes just rendered. The previous build's
    /// recorded hash is trusted only while the file's stamp (length and last write
    /// time) still matches what was recorded with it — the same short-circuit the skip
    /// checks use, so this reads no files.
    /// </summary>
    private static bool AlreadyOnDisk(string fullPath, BuildManifestPage? previous, string outputHash)
    {
        if (previous is null
            || !string.Equals(previous.OutputHash, outputHash, StringComparison.Ordinal)
            || previous.OutputLength is not { } length
            || previous.OutputLastWriteTimeUtc is not { } lastWriteTimeUtc)
        {
            return false;
        }

        var info = new FileInfo(fullPath);
        return info.Exists && info.Length == length && info.LastWriteTimeUtc == lastWriteTimeUtc;
    }

    internal static void ValidateNoStaticFileCollisions(ResolvedSitePaths options, IReadOnlyList<PageRenderRequest> pageRequests)
    {
        if (!Directory.Exists(options.StaticDirectory))
        {
            return;
        }

        var pageOutputPaths = pageRequests
            .Select(request => ResolvePageOutputPath(options.OutputDirectory, request.OutputRelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(options.StaticDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(options.StaticDirectory, file);
            var staticOutputPath = Path.GetFullPath(Path.Combine(options.OutputDirectory, relativePath));
            if (pageOutputPaths.Contains(staticOutputPath))
            {
                throw new InvalidOperationException(
                    $"Static file '{relativePath}' collides with a generated page output path. Rename the static file or change the page route.");
            }
        }
    }

    private static string ResolvePageOutputPath(string outputPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));
        var fullPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
        if (!fullPath.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Page output path '{relativePath}' escapes the output directory.");
        }

        return fullPath;
    }
}
