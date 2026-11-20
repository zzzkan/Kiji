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
    /// <param name="outputDirectoryCreated">
    /// The output directory did not exist when this build began, so no page has a file to
    /// compare with, and each page reads its stamp as it writes instead of reconciliation
    /// listing the directory afterwards.
    /// </param>
    internal static async Task<IReadOnlyList<RenderedPage>> RenderPagesAsync(
        ResolvedSitePaths options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        IReadOnlyDictionary<string, BuildManifestPage>? previousOutputs,
        CancellationToken cancellationToken,
        bool outputStampsValid = false,
        ILogger? logger = null,
        bool outputDirectoryCreated = false)
    {
        var log = logger ?? NullLogger.Instance;
        var resolvedPages = new (PageRenderRequest Request, string FullPath)[pageRequests.Count];
        for (var i = 0; i < pageRequests.Count; i++)
        {
            var fullPath = OutputPathValidator.ResolveUnderRoot(
                options.OutputDirectory,
                pageRequests[i].OutputRelativePath,
                "Page output path");
            resolvedPages[i] = (pageRequests[i], fullPath);
        }

        var outputDirectories = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        var rendered = new RenderedPage[resolvedPages.Length];

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
                rendered[index] = await WritePageAsync(request, fullPath, renderPageAsync, previous, outputStampsValid,
                    outputDirectoryCreated, outputDirectories, log, ct);
            });

        return rendered;
    }

    private static async Task<RenderedPage> WritePageAsync(
        PageRenderRequest pageRequest,
        string fullPath,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        BuildManifestPage? previous,
        bool outputStampsValid,
        bool outputDirectoryCreated,
        ConcurrentDictionary<string, byte> outputDirectories,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var writer = new PooledUtf8TextWriter();
        await renderPageAsync(pageRequest, writer, cancellationToken);
        var outputHash = writer.GetContentHash();

        var stamp = !outputDirectoryCreated && previous?.OutputHash == outputHash ? OutputStamp.Read(fullPath) : null;
        var written = outputDirectoryCreated || (previous is null
            ? !File.Exists(fullPath) || !writer.MatchesFile(fullPath)
            : previous.OutputHash != outputHash
                || (!(outputStampsValid && stamp is not null && stamp == previous.Stamp) && !writer.MatchesFile(fullPath)));
        if (written)
        {
            var directory = Path.GetDirectoryName(fullPath)!;
            if (!outputDirectories.ContainsKey(directory))
            {
                Directory.CreateDirectory(directory);
                outputDirectories.TryAdd(directory, 0);
            }
            writer.WriteToFile(fullPath);
        }
        var writtenStamp = outputDirectoryCreated ? OutputStamp.Read(fullPath) : null;

        if (written) { logger.PageGenerated(fullPath); }
        else { logger.PageUnchanged(fullPath); }
        return new RenderedPage(pageRequest, outputHash, written, writer.ToArray(), writtenStamp);
    }
}
