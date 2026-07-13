using System.Diagnostics;
using Kiji.Rendering;

namespace Kiji.Generation;

public static class StaticSiteGenerator
{
    public static async Task GenerateAsync(
        SsgOptions options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pageRequests);
        ArgumentNullException.ThrowIfNull(renderPageAsync);

        var stopwatch = Stopwatch.StartNew();

        ValidateNoStaticFileCollisions(options, pageRequests);

        await StaticFileCopier.CopyAsync(options.StaticPath, options.OutputPath);

        await RenderPagesAsync(options, pageRequests, renderPageAsync, cancellationToken);

        BuildOutput.Info($"Generated {pageRequests.Count} pages in {stopwatch.ElapsedMilliseconds} ms.");
    }

    internal static async Task<IReadOnlyList<RenderedPage>> RenderPagesAsync(
        SsgOptions options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        CancellationToken cancellationToken)
    {
        // Resolve every output path once and create the directory set up front, so
        // the parallel render loop issues no per-page directory syscalls.
        var resolvedPages = new (PageRenderRequest Request, string FullPath)[pageRequests.Count];
        var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pageRequests.Count; i++)
        {
            var fullPath = ResolvePageOutputPath(options.OutputPath, pageRequests[i].OutputRelativePath);
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
                rendered[index] = await WritePageAsync(request, fullPath, renderPageAsync, ct);
            });

        return rendered;
    }

    private static async Task<RenderedPage> WritePageAsync(
        PageRenderRequest pageRequest,
        string fullPath,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        CancellationToken cancellationToken)
    {
        // Render into a pooled UTF-8 buffer, then persist with one preallocated write:
        // no StreamWriter/FileStream buffers and no chunked async writes per page.
        using var writer = new PooledUtf8TextWriter();
        await renderPageAsync(pageRequest, writer, cancellationToken);
        var outputHash = writer.GetContentHash();
        writer.WriteToFile(fullPath);

        BuildOutput.Detail($"Generated: {fullPath}");
        return new RenderedPage(pageRequest, outputHash);
    }

    internal static void ValidateNoStaticFileCollisions(SsgOptions options, IReadOnlyList<PageRenderRequest> pageRequests)
    {
        if (!Directory.Exists(options.StaticPath))
        {
            return;
        }

        var pageOutputPaths = pageRequests
            .Select(request => ResolvePageOutputPath(options.OutputPath, request.OutputRelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(options.StaticPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(options.StaticPath, file);
            var staticOutputPath = Path.GetFullPath(Path.Combine(options.OutputPath, relativePath));
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
