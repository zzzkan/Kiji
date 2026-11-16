using System.Collections.Concurrent;
using Kiji.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kiji.Generation;

internal static class StaticSiteGenerator
{
    /// <param name="previousOutputs">
    /// What the last build recorded about each output path, keyed by relative path.
    /// Lets a page whose render produced the bytes already on disk skip the write.
    /// Null when nothing may be assumed about the output directory.
    /// </param>
    /// <param name="logger">Receives a Debug message per page.</param>
    internal static async Task<IReadOnlyList<RenderedPage>> RenderPagesAsync(
        ResolvedSitePaths options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        IReadOnlyDictionary<string, BuildManifestPage>? previousOutputs,
        CancellationToken cancellationToken,
        bool outputStampsValid = false,
        ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        // Resolve every output path once, before any page renders.
        var resolvedPages = new (PageRenderRequest Request, string FullPath)[pageRequests.Count];
        for (var i = 0; i < pageRequests.Count; i++)
        {
            var fullPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                pageRequests[i].OutputRelativePath,
                "Page output path");
            resolvedPages[i] = (pageRequests[i], fullPath);
        }

        // A page creates its directory when it writes, on the thread that rendered it:
        // creating them all first is a serial pass over the file system, as long on a real
        // disk as writing the pages. Each directory is still created once.
        var outputDirectories = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

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
                rendered[index] = await WritePageAsync(request, fullPath, renderPageAsync, previous, outputStampsValid, outputDirectories, log, ct);
            });

        return rendered;
    }

    private static async Task<RenderedPage> WritePageAsync(
        PageRenderRequest pageRequest,
        string fullPath,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        BuildManifestPage? previous,
        bool outputStampsValid,
        ConcurrentDictionary<string, byte> outputDirectories,
        ILogger logger,
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
        // A forced render or missing cache still produces verified fresh bytes.
        // Keep identical files so downstream SDK compression remains incremental.
        var written = previous is null
            ? !File.Exists(fullPath) || !writer.MatchesFile(fullPath)
            : previous.OutputHash != outputHash
                || (!(outputStampsValid && stamp is not null && stamp == previous.Stamp) && !writer.MatchesFile(fullPath));
        if (written)
        {
            var directory = Path.GetDirectoryName(fullPath)!;
            // Recorded only once it exists, so a page racing for the same directory creates it too.
            if (!outputDirectories.ContainsKey(directory))
            {
                Directory.CreateDirectory(directory);
                outputDirectories.TryAdd(directory, 0);
            }
            writer.WriteToFile(fullPath);
        }

        if (written) { logger.PageGenerated(fullPath); }
        else { logger.PageUnchanged(fullPath); }
        return new RenderedPage(pageRequest, outputHash, written, writer.ToArray());
    }

}
