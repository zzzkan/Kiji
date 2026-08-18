using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Hashing;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kiji.Assets;
using Kiji.Rendering;

namespace Kiji.Markdown;

/// <summary>
/// Processes markdown bodies into HTML through a shared Markdig pipeline. Referenced
/// local images are materialized into the output directory of the page being rendered
/// (see <see cref="PageRenderContext"/>) and rewritten to <c>./</c>-relative URLs.
/// </summary>
public sealed class MarkdownProcessor
{
    private readonly MarkdownPipeline _pipeline;
    private readonly ConcurrentBag<PooledMarkdigRenderer> _rendererPool = [];
    private readonly IImageAssetProcessor _imageAssetProcessor;
    private readonly IReadOnlyList<Func<string, string>> _htmlPostProcessors;
    private readonly string _outputPath;
    private readonly string? _imageCachePath;
    private readonly string? _imageCssClass;

    /// <param name="options">The resolved site paths.</param>
    /// <param name="imageAssetProcessor">The image backend used to process referenced local images.</param>
    /// <param name="contentOptions">Optional pipeline and post-processing configuration.</param>
    public MarkdownProcessor(
        SsgOptions options,
        IImageAssetProcessor imageAssetProcessor,
        MarkdownProcessingOptions? contentOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(imageAssetProcessor);

        _imageAssetProcessor = imageAssetProcessor;
        _outputPath = options.OutputPath;
        _imageCachePath = options.ImageCachePath;
        _htmlPostProcessors = contentOptions is null ? [] : [.. contentOptions.HtmlPostProcessors];
        _imageCssClass = contentOptions?.ImageCssClass;
        _pipeline = BuildPipeline(contentOptions);
    }

    /// <summary>
    /// Processes a markdown file body, materializing referenced images into the current
    /// page's output directory and converting the result to HTML.
    /// </summary>
    /// <param name="filePath">Path to the markdown file.</param>
    public async Task<string> ProcessAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var markdownBody = MarkdownFrontMatterParser.RemoveFrontMatter(content);
        return await ProcessBodyAsync(filePath, markdownBody, cancellationToken);
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

        PageRenderContext.Current?.Dependencies?.AddFile(Path.GetFullPath(filePath));

        var document = global::Markdig.Markdown.Parse(markdownBody, _pipeline);

        var imageInfoLookup = await MaterializeReferencedImagesAsync(filePath, document, cancellationToken);
        var imageContext = new ResponsiveImageContext(imageInfoLookup, _imageCssClass);

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
            var cacheDirectory = _imageCachePath is null
                ? null
                : Path.Combine(_imageCachePath, CreateCacheKey(Path.GetDirectoryName(sourceFile)!));

            pageContext.Dependencies?.AddFile(sourceFile);

            var processed = await _imageAssetProcessor.ProcessImageAsync(
                sourceFile,
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

        if (!File.Exists(sourceFile))
        {
            throw new InvalidOperationException(
                $"Image '{url}' referenced by '{filePath}' was not found at '{sourceFile}'.");
        }

        return sourceFile;
    }

    private static string GetDirectoryPart(string referenceKey)
    {
        var separatorIndex = referenceKey.LastIndexOf('/');
        return separatorIndex >= 0 ? referenceKey[..separatorIndex] : string.Empty;
    }

    private static string CreateCacheKey(string sourceDirectory)
    {
        // Groups cache entries per source directory so stale-variant cleanup for one
        // post never touches another post's files. Not a security boundary.
        var hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(Path.GetFullPath(sourceDirectory).ToUpperInvariant()));
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private string Render(MarkdownDocument document, ResponsiveImageContext imageContext)
    {
        // Renderers are pooled per processor: setup costs ~3x the render itself.
        // Pool size is bounded by concurrent renders (≤ CPU count); an entry is
        // dropped instead of returned if its render threw, so a renderer left in an
        // unknown state is never reused.
        if (!_rendererPool.TryTake(out var pooled))
        {
            pooled = PooledMarkdigRenderer.Create(_pipeline);
        }

        var html = pooled.Render(document, imageContext);
        _rendererPool.Add(pooled);
        return html;
    }

    private static MarkdownPipeline BuildPipeline(MarkdownProcessingOptions? contentOptions)
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
