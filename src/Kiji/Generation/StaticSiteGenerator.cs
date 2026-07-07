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

        CleanupLegacyNotFoundOutput(options);

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
        var fullPath = Path.Combine(options.OutputPath, pageRequest.OutputRelativePath);
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

    private static void CleanupLegacyNotFoundOutput(SsgOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var legacyNotFoundDirectory = Path.Combine(options.OutputPath, "not-found");
        var legacyNotFoundPath = Path.Combine(legacyNotFoundDirectory, "index.html");
        if (!File.Exists(legacyNotFoundPath))
        {
            return;
        }

        File.Delete(legacyNotFoundPath);
        if (!Directory.EnumerateFileSystemEntries(legacyNotFoundDirectory).Any())
        {
            Directory.Delete(legacyNotFoundDirectory);
        }

        Console.WriteLine($"Removed legacy output: {legacyNotFoundPath}");
    }
}
