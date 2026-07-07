using System.Globalization;
using System.IO.Hashing;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Processes markdown bodies into HTML through a shared Markdig pipeline, optimizing
/// referenced local images when an image backend is registered.
/// </summary>
public sealed class MarkdownProcessor
{
    private readonly MarkdownPipeline _pipeline;
    private readonly IImageAssetProcessor _imageAssetProcessor;
    private readonly bool _optimizeImages;
    private readonly IReadOnlyList<Func<string, string>> _htmlPostProcessors;
    private readonly string _assetsOutputDirectory;
    private readonly string _assetsBaseUrl;

    /// <param name="options">The resolved site paths.</param>
    /// <param name="imageAssetProcessor">
    /// The image backend. When this is a <see cref="NullImageAssetProcessor"/>, image
    /// collection, optimization, and responsive markup are skipped entirely.
    /// </param>
    /// <param name="contentOptions">Optional pipeline and post-processing configuration.</param>
    public MarkdownProcessor(
        SsgOptions options,
        IImageAssetProcessor imageAssetProcessor,
        MarkdownContentOptions? contentOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(imageAssetProcessor);

        _imageAssetProcessor = imageAssetProcessor;
        _optimizeImages = imageAssetProcessor is not NullImageAssetProcessor;
        _assetsOutputDirectory = Path.Combine(options.OutputPath, options.AssetsDirectoryName);
        _assetsBaseUrl = options.AssetsDirectoryName;
        _htmlPostProcessors = contentOptions is null ? [] : [.. contentOptions.HtmlPostProcessors];
        _pipeline = BuildPipeline(contentOptions);
    }

    /// <summary>
    /// Processes a markdown file body, optimizing referenced images and converting the result to HTML.
    /// </summary>
    /// <param name="filePath">Path to the markdown file.</param>
    public async Task<string> ProcessAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var markdownBody = MarkdownFrontMatterParser.RemoveFrontMatter(content);
        var document = global::Markdig.Markdown.Parse(markdownBody, _pipeline);

        ResponsiveImageContext? imageContext = null;
        if (_optimizeImages)
        {
            var contentKey = CreateContentKey(markdownBody);
            var sourceDirectory = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException($"Cannot determine source directory for markdown file: {filePath}");
            var outputDirectory = Path.Combine(_assetsOutputDirectory, contentKey);
            var imageUrls = CollectLocalImageUrls(document);
            var imageInfoLookup = await _imageAssetProcessor.ProcessReferencedImagesAsync(
                outputDirectory,
                sourceDirectory,
                imageUrls,
                cancellationToken);

            imageContext = new ResponsiveImageContext(imageInfoLookup, _assetsBaseUrl, contentKey);
        }

        var html = Render(document, imageContext);

        foreach (var postProcess in _htmlPostProcessors)
        {
            html = postProcess(html);
        }

        return html;
    }

    private string Render(MarkdownDocument document, ResponsiveImageContext? imageContext)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        _pipeline.Setup(renderer);

        if (imageContext is not null)
        {
            ResponsiveImageWriter.Attach(renderer, imageContext);
        }

        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static MarkdownPipeline BuildPipeline(MarkdownContentOptions? contentOptions)
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

    private static string CreateContentKey(string markdownBody)
    {
        // Cache-busting key for the per-content asset directory, not a security boundary.
        var hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(markdownBody));
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
