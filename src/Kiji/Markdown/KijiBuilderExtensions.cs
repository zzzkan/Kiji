using Kiji.Assets;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Markdown content support for <see cref="KijiBuilder"/>.
/// </summary>
public static class KijiBuilderExtensions
{
    /// <summary>
    /// Adds a markdown content source over the configured content directory.
    /// Every <c>*.md</c> file is discovered recursively; front matter is parsed into
    /// <typeparamref name="TFrontMatter"/> and bodies render lazily through the markdown
    /// pipeline (with image optimization when an image backend is registered).
    /// </summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="configure">
    /// Optional configuration of the markdown pipeline, HTML post-processing, and
    /// front matter deserialization. See <see cref="MarkdownContentOptions"/>.
    /// </param>
    public static ContentCollection<MarkdownContent<TFrontMatter>> AddMarkdownContent<TFrontMatter>(
        this KijiBuilder builder,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        var contentOptions = new MarkdownContentOptions();
        configure?.Invoke(contentOptions);

        // A factory rather than a shared instance: front matter parsing runs on
        // multiple threads and YamlDotNet deserializers are not documented as thread-safe.
        IDeserializer CreateFrontMatterDeserializer()
        {
            return MarkdownFrontMatterParser.CreateDeserializer(contentOptions.FrontMatterConfigurations);
        }

        // Created once per registration so it survives content re-materializations
        // in the dev server; a file save re-parses only the files that changed.
        var frontMatterCache = new MarkdownFrontMatterCache<TFrontMatter>();

        return builder.AddContentSource(services =>
        {
            var options = services.GetRequiredService<SsgOptions>();
            var imageAssetProcessor = services.GetRequiredService<IImageAssetProcessor>();
            var markdownProcessor = new MarkdownProcessor(options, imageAssetProcessor, contentOptions);

            return new MarkdownContentsBuilder<TFrontMatter>(
                options.ContentsPath,
                (content, cancellationToken) => markdownProcessor.ProcessAsync(content.FileInfo.FilePath, cancellationToken),
                CreateFrontMatterDeserializer,
                frontMatterCache)
                .Build();
        });
    }
}
