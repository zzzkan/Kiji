using System.Text;
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

        Console.WriteLine("=== Site Generation ===");

        ValidateNoStaticFileCollisions(options, pageRequests);

        await StaticFileCopier.CopyAsync(options.StaticPath, options.OutputPath);

        // Each page renders on its own HtmlRenderer/DI scope, so pages are safe to
        // render concurrently.
        await Parallel.ForEachAsync(
            pageRequests,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (pageRequest, ct) =>
            {
                await WritePageAsync(options, pageRequest, renderPageAsync, ct);
            });

        Console.WriteLine("Site generation complete.");
    }

    private static async Task WritePageAsync(
        SsgOptions options,
        PageRenderRequest pageRequest,
        Func<PageRenderRequest, TextWriter, CancellationToken, Task> renderPageAsync,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolvePageOutputPath(options.OutputPath, pageRequest.OutputRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        var writer = new StreamWriter(stream, Encoding.UTF8);
        await using (stream)
        await using (writer)
        {
            await renderPageAsync(pageRequest, writer, cancellationToken);
        }

        Console.WriteLine($"Generated: {fullPath}");
    }

    private static void ValidateNoStaticFileCollisions(SsgOptions options, IReadOnlyList<PageRenderRequest> pageRequests)
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
