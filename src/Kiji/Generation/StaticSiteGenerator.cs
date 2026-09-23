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
        CancellationToken cancellationToken,
        bool outputStampsValid = false)
    {
        // Resolve every output path once and create the directory set up front, so
        // the parallel render loop issues no per-page directory syscalls.
        var resolvedPages = new (PageRenderRequest Request, string FullPath)[pageRequests.Count];
        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pageRequests.Count; i++)
        {
            var fullPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                pageRequests[i].OutputRelativePath,
                "Page output path");
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
                rendered[index] = await WritePageAsync(request, fullPath, renderPageAsync, previous, outputStampsValid, ct);
            });

        return rendered;
    }

    private static async Task<RenderedPage> WritePageAsync(
        PageRenderRequest pageRequest,
        string fullPath,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        BuildManifestPage? previous,
        bool outputStampsValid,
        CancellationToken cancellationToken)
    {
        // Render into a pooled UTF-8 buffer, then persist with one write:
        // no StreamWriter/FileStream buffers and no chunked async writes per page.
        using var writer = new PooledUtf8TextWriter();
        await renderPageAsync(pageRequest, writer, cancellationToken);
        var outputHash = writer.GetContentHash();

        // Kiji owns generated outputs. Matching stamps avoid reopening unchanged
        // files, but newly rendered bytes must still match the recorded hash.
        var stamp = previous?.OutputHash == outputHash ? OutputStamp.Read(fullPath) : null;
        var written = previous?.OutputHash != outputHash
            || (!(outputStampsValid && stamp is not null && stamp == previous.Stamp) && !writer.MatchesFile(fullPath));
        if (written)
        {
            writer.WriteToFile(fullPath);
        }

        BuildOutput.Detail($"{(written ? "Generated" : "Unchanged")}: {fullPath}");
        return new RenderedPage(pageRequest, outputHash, written, writer.ToArray());
    }

    internal static void ValidateNoStaticFileCollisions(ResolvedSitePaths options, IReadOnlyList<PageRenderRequest> pageRequests)
    {
        if (!Directory.Exists(options.StaticDirectory))
        {
            return;
        }

        var pageOutputPaths = pageRequests
            .Select(request => OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                request.OutputRelativePath,
                "Page output path"))
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

}
