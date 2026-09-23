using System.Collections.Concurrent;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kiji.Assets;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>
/// Processes markdown bodies into HTML through a shared Markdig pipeline. Referenced
/// local images are materialized into the output directory of the page being rendered
/// (see <see cref="PageRenderContext"/>) and rewritten to root-relative public URLs.
/// </summary>
internal sealed class MarkdownProcessor
{
    private readonly MarkdownPipeline _pipeline;
    private readonly ConcurrentBag<PooledMarkdigRenderer> _rendererPool = [];
    private readonly IImageProcessor _imageProcessor;
    private readonly IReadOnlyList<Func<string, string>> _htmlPostProcessors;
    private readonly string _outputPath;
    private readonly string? _imageCachePath;

    /// <param name="options">The resolved site paths.</param>
    /// <param name="imageProcessor">The image backend used to process referenced local images.</param>
    /// <param name="contentOptions">Optional pipeline and post-processing configuration.</param>
    public MarkdownProcessor(
        ResolvedSitePaths options,
        IImageProcessor imageProcessor,
        MarkdownOptions? contentOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(imageProcessor);

        _imageProcessor = imageProcessor;
        _outputPath = options.OutputDirectory;
        _imageCachePath = options.ImageCacheDirectory;
        _htmlPostProcessors = contentOptions is null ? [] : [.. contentOptions.HtmlPostProcessors];
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

        cancellationToken.ThrowIfCancellationRequested();

        // MarkdownContent records the source hash of this already-read body.
        // Reopening the file here would duplicate input verification and could
        // fingerprint bytes different from the body being converted.
        var document = global::Markdig.Markdown.Parse(markdownBody, _pipeline);

        var imageInfoLookup = await MaterializeReferencedImagesAsync(filePath, document, cancellationToken);
        var imageContext = new ResponsiveImageContext(
            imageInfoLookup,
            PageRenderContext.Current?.OutputUrlDirectory ?? "/");

        var html = Render(document, imageContext);

        foreach (var postProcess in _htmlPostProcessors)
        {
            html = postProcess(html);
        }

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
                $"Markdown file '{filePath}' references local images and must be rendered within a page render.");

        var sourceDirectory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Cannot determine source directory for markdown file: {filePath}");
        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var pageOutputDirectory = Path.Combine(_outputPath, pageContext.OutputRelativeDirectory);

        foreach (var url in imageUrls)
        {
            var referenceKey = ImageReferenceKey.FromMarkdownUrl(url);
            var sourceFile = ResolveSourceFile(filePath, url, referenceKey, sourceRoot);

            var relativeDirectory = GetDirectoryPart(referenceKey);
            var outputDirectory = Path.Combine(pageOutputDirectory, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            var cacheDirectory = pageContext.Dependencies is null ? null : _imageCachePath;

            var processed = await ImageArtifactProcessor.ProcessAsync(
                _imageProcessor, sourceFile,
                outputDirectory,
                cacheDirectory,
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

    private static string ResolveSourceFile(string filePath, string url, string referenceKey, string sourceRoot)
    {
        if (referenceKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"Image reference '{url}' in '{filePath}' is not a resolvable relative path.");
        }

        var sourceFile = Path.GetFullPath(Path.Combine(sourceRoot, referenceKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!sourceFile.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Image reference '{url}' in '{filePath}' resolves outside the markdown file's directory. " +
                "Place images beside the markdown file (or in a subdirectory next to it).");
        }

        return !File.Exists(sourceFile)
            ? throw new InvalidOperationException(
                $"Image '{url}' referenced by '{filePath}' was not found at '{sourceFile}'.")
            : sourceFile;
    }

    private static string GetDirectoryPart(string referenceKey)
    {
        var separatorIndex = referenceKey.LastIndexOf('/');
        return separatorIndex >= 0 ? referenceKey[..separatorIndex] : string.Empty;
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
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Use(new SecureLinkExtension());

        if (contentOptions is not null)
        {
            foreach (var configure in contentOptions.PipelineConfigurations)
            {
                configure(builder);
            }
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
