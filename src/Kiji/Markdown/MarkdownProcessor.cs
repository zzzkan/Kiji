using System.Collections.Concurrent;
using Markdig;
using Markdig.Extensions.GenericAttributes;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kiji.Assets;
using Kiji.Generation;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>
/// Processes markdown bodies into HTML through a shared Markdig pipeline. Referenced
/// local images are materialized into shared site output and rewritten to
/// root-relative public URLs.
/// </summary>
internal sealed class MarkdownProcessor
{
    private readonly MarkdownPipeline _pipeline;
    private readonly ConcurrentBag<PooledMarkdigRenderer> _rendererPool = [];
    private readonly IImageProcessor _imageProcessor;
    private readonly string _outputPath;
    private readonly string? _imageCachePath;
    private readonly string _projectDirectory;
    private readonly string _workDirectory;
    private readonly string _basePath;
    private readonly ContentPath _contentPaths;

    /// <param name="options">The resolved site paths.</param>
    /// <param name="paths">The project root used for portable source identities.</param>
    /// <param name="site">The site's public deployment base path.</param>
    /// <param name="contentPaths">Registered content roots used to validate local image sources.</param>
    /// <param name="imageProcessor">The image backend used to process referenced local images.</param>
    /// <param name="contentOptions">Site-wide pipeline configuration.</param>
    public MarkdownProcessor(
        ResolvedSitePaths options,
        SiteExecutionPaths paths,
        SiteInfo site,
        ContentPath contentPaths,
        IImageProcessor imageProcessor,
        MarkdownOptions? contentOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(imageProcessor);

        _imageProcessor = imageProcessor;
        _contentPaths = contentPaths;
        _outputPath = Path.Combine(options.OutputDirectory, "_images");
        _imageCachePath = options.ImageCacheDirectory;
        _projectDirectory = paths.ProjectDirectory;
        _workDirectory = paths.ResolveImageWorkPath(options);
        _basePath = new Uri(site.BaseUrl, "_images/").AbsolutePath;
        _pipeline = BuildPipeline(contentOptions);
    }

    /// <summary>
    /// Processes an already-read markdown body for the given source file, skipping the
    /// file read. <paramref name="filePath"/> still identifies the source for image
    /// resolution and dependency tracking.
    /// </summary>
    /// <param name="filePath">Path to the markdown file the body came from.</param>
    /// <param name="markdownBody">The markdown body with front matter already removed.</param>
    public async Task<string> ProcessBodyAsync(string filePath, string markdownBody, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(markdownBody);
        filePath = Path.GetFullPath(filePath, _projectDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        // MarkdownContent records the source hash of this already-read body.
        // Reopening the file here would duplicate input verification and could
        // fingerprint bytes different from the body being converted.
        var document = global::Markdig.Markdown.Parse(markdownBody, _pipeline);
        cancellationToken.ThrowIfCancellationRequested();

        var imageInfoLookup = await MaterializeReferencedImagesAsync(filePath, document, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var imageContext = new ResponsiveImageContext(imageInfoLookup, _basePath);

        var html = Render(document, imageContext);

        cancellationToken.ThrowIfCancellationRequested();
        return html;
    }

    private async Task<IReadOnlyDictionary<string, ProcessedImageInfo>> MaterializeReferencedImagesAsync(
        string filePath,
        MarkdownDocument document,
        CancellationToken cancellationToken)
    {
        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>(StringComparer.Ordinal);
        var imageUrls = CollectLocalImageUrls(document);
        if (imageUrls.Count == 0)
        {
            return imageInfoLookup;
        }

        var pageContext = PageRenderContext.Current
            ?? throw new InvalidOperationException(
                $"Markdown file '{filePath}' references local images and must be rendered within a page render, not in a content projection or a factory.");

        var sourceDirectory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Cannot determine source directory for markdown file: {filePath}");

        foreach (var url in imageUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var referenceKey = ImageReferenceKey.FromMarkdownUrl(url);
            var sourceFile = ResolveSourceFile(filePath, url, referenceKey, sourceDirectory);

            var outputDirectory = _outputPath;
            var cacheDirectory = pageContext.Dependencies is null ? null : _imageCachePath;

            var processed = await ImageArtifactProcessor.ProcessAsync(
                _imageProcessor, sourceFile,
                outputDirectory,
                cacheDirectory, _workDirectory,
                cancellationToken);

            if (pageContext.Dependencies is { } dependencies)
            {
                foreach (var variant in processed.Variants)
                {
                    dependencies.AddOutput(Path.Combine(outputDirectory, variant.FileName));
                }
            }

            imageInfoLookup[referenceKey] = processed;
        }

        return imageInfoLookup;
    }

    private string ResolveSourceFile(string filePath, string url, string referenceKey, string sourceRoot)
    {
        if (referenceKey.Length == 0 || Path.IsPathRooted(referenceKey))
        {
            throw new InvalidOperationException(
                $"Image reference '{url}' in '{filePath}' is not a resolvable relative path.");
        }

        try
        {
            return _contentPaths.ResolveFile(
                Path.GetFullPath(referenceKey.Replace('/', Path.DirectorySeparatorChar), sourceRoot));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException($"Image '{url}' referenced by '{filePath}' cannot be resolved. {exception.Message}", exception);
        }
    }

    private string Render(MarkdownDocument document, ResponsiveImageContext imageContext)
    {
        // A failed renderer may retain partial state; do not return it to the pool.
        if (!_rendererPool.TryTake(out var pooled))
        {
            pooled = PooledMarkdigRenderer.Create(_pipeline);
        }

        var html = pooled.Render(document, imageContext);
        _rendererPool.Add(pooled);
        return html;
    }

    private static MarkdownPipeline BuildPipeline(MarkdownOptions? contentOptions)
    {
        var builder = (contentOptions ?? new MarkdownOptions()).Pipeline;
        var attributes = builder.Extensions.Find<GenericAttributesExtension>();
        if (attributes is not null)
        {
            builder.Extensions.Remove(attributes);
            builder.Extensions.Add(attributes);
        }

        return builder.Build();
    }

    private static IReadOnlyList<string> CollectLocalImageUrls(MarkdownDocument document)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in document)
        {
            CollectLocalImageUrls(block, urls);
        }

        return [.. urls];
    }

    private static void CollectLocalImageUrls(Block block, ISet<string> urls)
    {
        switch (block)
        {
            case LeafBlock { Inline: not null } leafBlock:
                CollectLocalImageUrls(leafBlock.Inline, urls);
                break;

            case ContainerBlock containerBlock:
                foreach (var child in containerBlock)
                {
                    CollectLocalImageUrls(child, urls);
                }
                break;
        }
    }

    private static void CollectLocalImageUrls(ContainerInline container, ISet<string> urls)
    {
        for (var current = container.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LinkInline { IsImage: true, Url: not null } linkInline when LocalImageUrl.IsLocalImage(linkInline.Url):
                    urls.Add(linkInline.Url);
                    break;

                case ContainerInline childContainer:
                    CollectLocalImageUrls(childContainer, urls);
                    break;
            }
        }
    }
}
