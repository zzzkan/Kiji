using System.Collections.Concurrent;
using System.Text;
using Kiji.Rendering;

namespace Kiji.Generation;

public static class StaticSiteGenerator
{
    public static async Task GenerateAsync(
        SsgOptions options,
        Uri baseUrl,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, CancellationToken, Task<string>> renderPageAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(pageRequests);
        ArgumentNullException.ThrowIfNull(renderPageAsync);

        Console.WriteLine("=== Site Generation ===");

        CleanupLegacyNotFoundOutput(options);

        await StaticFileCopier.CopyAsync(options.StaticPath, options.OutputPath);

        var sitemapRoutes = await RenderPagesAsync(
            options,
            pageRequests,
            renderPageAsync,
            cancellationToken);

        await GenerateSitemapAsync(options, baseUrl, sitemapRoutes);

        Console.WriteLine("Site generation complete.");
    }

    private static async Task<HashSet<string>> RenderPagesAsync(
        SsgOptions options,
        IReadOnlyList<PageRenderRequest> pageRequests,
        Func<PageRenderRequest, CancellationToken, Task<string>> renderPageAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pageRequests);
        ArgumentNullException.ThrowIfNull(renderPageAsync);

        // Each page renders on its own HtmlRenderer/DI scope, so pages are safe to
        // render concurrently. The sitemap is sorted afterwards, keeping output
        // deterministic regardless of completion order.
        var sitemapRoutes = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            pageRequests,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (pageRequest, ct) =>
            {
                var html = await renderPageAsync(pageRequest, ct);
                await WritePageAsync(options, pageRequest.OutputRelativePath, html);

                if (!pageRequest.ExcludeFromSitemap)
                {
                    sitemapRoutes.Add(pageRequest.RoutePath);
                }
            });

        return new HashSet<string>(sitemapRoutes, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task GenerateSitemapAsync(
        SsgOptions options,
        Uri baseUrl,
        IReadOnlyCollection<string> sitemapRoutes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(sitemapRoutes);

        var sitemapUrls = sitemapRoutes
            .OrderBy(static route => route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await SitemapGenerator.GenerateAsync(options.OutputPath, baseUrl, sitemapUrls);
    }

    private static async Task WritePageAsync(SsgOptions options, string relativePath, string html)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(html);

        var fullPath = Path.Combine(options.OutputPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, html, Encoding.UTF8);
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
