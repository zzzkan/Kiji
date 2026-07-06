using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Kiji.Assets;

namespace Kiji.Markdown;

/// <summary>
/// Processes markdown bodies into HTML and optimizes referenced local images during rendering.
/// </summary>
public sealed class MarkdownProcessor(SsgOptions options, IImageAssetProcessor imageAssetProcessor)
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private readonly string _assetsOutputDirectory = Path.Combine(options.OutputPath, options.AssetsDirectoryName);
    private readonly string _assetsBaseUrl = options.AssetsDirectoryName;

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
        var contentKey = CreateContentKey(markdownBody);
        var sourceDirectory = Path.GetDirectoryName(filePath)
            ?? throw new InvalidOperationException($"Cannot determine source directory for markdown file: {filePath}");
        var outputDirectory = Path.Combine(_assetsOutputDirectory, contentKey);
        var imageUrls = CollectLocalImageUrls(markdownBody);
        var imageInfoLookup = await imageAssetProcessor.ProcessReferencedImagesAsync(
            outputDirectory,
            sourceDirectory,
            imageUrls,
            cancellationToken);

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Use(new SecureLinkExtension())
            .Use(new ResponsiveImageExtension(imageInfoLookup, _assetsBaseUrl, contentKey))
            .Build();

        return global::Markdig.Markdown.ToHtml(markdownBody, pipeline);
    }

    private static IReadOnlyList<string> CollectLocalImageUrls(string markdownBody)
    {
        var document = global::Markdig.Markdown.Parse(markdownBody);
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
                case LinkInline { IsImage: true, Url: not null } linkInline when IsLocalImage(linkInline.Url):
                    urls.Add(linkInline.Url);
                    break;

                case ContainerInline childContainer:
                    CollectLocalImageUrls(childContainer, urls);
                    break;
            }
        }
    }

    private static bool IsLocalImage(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(url).ToLowerInvariant();
        return ImageExtensions.Contains(extension);
    }

    private static string CreateContentKey(string markdownBody)
    {
        var bytes = Encoding.UTF8.GetBytes(markdownBody);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
