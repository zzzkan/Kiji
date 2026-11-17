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

        var rendered = new (PageRenderRequest Request, string OutputHash, byte[] Html)[resolvedPages.Length];
        var written = new Task<bool>[resolvedPages.Length];

        // Each page renders on its own HtmlRenderer/DI scope, so pages are safe to
        // render concurrently. Pages persist on the writer's threads, which also create
        // each output directory once, when its first page is written.
        using (var output = new PageOutputWriter(cancellationToken))
        {
            try
            {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, resolvedPages.Length),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = output.Stopping,
                    },
                    async (index, ct) =>
                    {
                        var (request, fullPath) = resolvedPages[index];
                        var previous = previousOutputs is not null
                            && previousOutputs.TryGetValue(request.OutputRelativePath, out var recorded)
                                ? recorded
                                : null;
                        rendered[index] = await RenderPageAsync(request, renderPageAsync, ct);
                        written[index] = output.Enqueue(fullPath, rendered[index].Html,
                            () => MustWrite(fullPath, rendered[index].OutputHash, rendered[index].Html, previous, outputStampsValid));
                    });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A page failed to persist; its exception is the one to report, below.
            }
        }

        // Reports a failed write, including when it stopped rendering early.
        await Task.WhenAll(written.Where(static task => task is not null));
        var pages = new RenderedPage[resolvedPages.Length];
        for (var i = 0; i < pages.Length; i++)
        {
            var (request, outputHash, html) = rendered[i];
            var pageWritten = await written[i];
            var fullPath = resolvedPages[i].FullPath;
            if (pageWritten) { log.PageGenerated(fullPath); }
            else { log.PageUnchanged(fullPath); }
            pages[i] = new RenderedPage(request, outputHash, pageWritten, html);
        }
        return pages;
    }

    private static async Task<(PageRenderRequest Request, string OutputHash, byte[] Html)> RenderPageAsync(
        PageRenderRequest pageRequest,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        CancellationToken cancellationToken)
    {
        // Render into a pooled UTF-8 buffer; the page persists with one write.
        using var writer = new PooledUtf8TextWriter();
        await renderPageAsync(pageRequest, writer, cancellationToken);
        return (pageRequest, writer.GetContentHash(), writer.ToArray());
    }

    /// <summary>Whether the rendered bytes must replace what is on disk.</summary>
    private static bool MustWrite(string fullPath, string outputHash, byte[] html,
        BuildManifestPage? previous, bool outputStampsValid)
    {
        // A forced render or missing cache still produces verified fresh bytes.
        // Keep identical files so downstream SDK compression remains incremental.
        if (previous is null)
        {
            return !File.Exists(fullPath) || !BuildFingerprint.FileEquals(fullPath, html);
        }
        // Kiji owns generated outputs. Matching stamps avoid reopening unchanged
        // files, but newly rendered bytes must still match the recorded hash.
        if (previous.OutputHash != outputHash) { return true; }
        var stamp = OutputStamp.Read(fullPath);
        return !(outputStampsValid && stamp is not null && stamp == previous.Stamp)
            && !BuildFingerprint.FileEquals(fullPath, html);
    }
}
