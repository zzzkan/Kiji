using System.Collections.Concurrent;
using Kiji.Generation;
using Kiji.Images;
using Kiji.Paths;
using Kiji.Rendering;
using Markdig.Extensions.GenericAttributes;
using Markdig.Syntax.Inlines;
using Markdig.Syntax;
using Markdig;

namespace Kiji.Markdown;

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

    public async Task<string> ProcessBodyAsync(string filePath, string markdownBody, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(markdownBody);
        filePath = Path.GetFullPath(filePath, _projectDirectory);

        cancellationToken.ThrowIfCancellationRequested();

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
        if (!_rendererPool.TryTake(out var pooled))
        {
            pooled = PooledMarkdigRenderer.Create(_pipeline);
        }

        var html = pooled.Render(document, imageContext);
        _rendererPool.Add(pooled);
        return html;
    }

    internal static MarkdownPipeline BuildPipeline(MarkdownOptions? contentOptions)
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
