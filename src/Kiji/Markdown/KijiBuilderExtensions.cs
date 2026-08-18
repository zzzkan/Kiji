using Kiji.Assets;
using Kiji.Generation;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Markdown content support for <see cref="KijiBuilder"/>.
/// </summary>
public static class KijiBuilderExtensions
{
    /// <summary>
    /// Adds a markdown content source. Every <c>*.md</c> under
    /// <see cref="MarkdownContentOptions{TModel}.Directory"/> (the content directory by
    /// default) is discovered recursively; front matter is parsed into
    /// <typeparamref name="TFrontMatter"/> and bodies render lazily through the markdown
    /// pipeline (with image optimization when an image backend is registered).
    /// </summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="key">
    /// Identifies each item. Keys must be non-empty and unique.
    /// <see cref="MarkdownFileInfo.Slug"/> is the usual choice.
    /// </param>
    /// <param name="configure">
    /// Declares which files the source reads, how its items are validated, and how
    /// markdown and front matter are processed. See <see cref="MarkdownContentOptions{TModel}"/>.
    /// </param>
    /// <returns>
    /// The builder. The dictionary is resolved by its element type:
    /// <c>@inject ContentDictionary&lt;MarkdownContent&lt;TFrontMatter&gt;&gt;</c>.
    /// </returns>
    public static KijiBuilder AddMarkdownContent<TFrontMatter>(
        this KijiBuilder builder,
        Func<MarkdownContent<TFrontMatter>, string> key,
        Action<MarkdownContentOptions<MarkdownContent<TFrontMatter>>>? configure = null)
        where TFrontMatter : class
    {
        return builder.AddMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            key,
            configure);
    }

    /// <summary>
    /// Adds a markdown content source projected into <typeparamref name="TModel"/>.
    /// The projection is where a raw markdown file becomes your own model, so it is
    /// also the natural place to reject content that does not belong — throw from it,
    /// or declare checks with <see cref="ContentSourceOptions{T}.Validate(Func{T, bool}, string)"/>
    /// to have every failure reported at once.
    /// </summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="select">
    /// Projects a parsed markdown file into the model. It is applied per item, so each
    /// model keeps the source file of the markdown it came from and a keyed lookup stays
    /// a single-file dependency. Do not let it depend on other items.
    /// </param>
    /// <param name="key">Identifies each item. Keys must be non-empty and unique.</param>
    /// <param name="configure">
    /// Declares which files the source reads, how its items are validated, and how
    /// markdown and front matter are processed.
    /// </param>
    /// <returns>
    /// The builder. The dictionary is resolved by its element type:
    /// <c>@inject ContentDictionary&lt;TModel&gt;</c>.
    /// </returns>
    public static KijiBuilder AddMarkdownContent<TFrontMatter, TModel>(
        this KijiBuilder builder,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Func<TModel, string> key,
        Action<MarkdownContentOptions<TModel>>? configure = null)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(key);

        var contentOptions = new MarkdownContentOptions<TModel>();
        configure?.Invoke(contentOptions);

        // A factory rather than a shared instance: front matter parsing runs on
        // multiple threads and YamlDotNet deserializers are not documented as thread-safe.
        IDeserializer CreateFrontMatterDeserializer()
        {
            return MarkdownFrontMatterParser.CreateDeserializer(contentOptions.FrontMatterConfigurations);
        }

        // Created once per registration so it survives content re-materializations
        // in the dev server; a file save re-reads only the files that changed.
        var sourceCache = new MarkdownSourceCache<TFrontMatter>();

        return builder.AddContentSource(
            services =>
            {
                var options = services.GetRequiredService<SsgOptions>();
                var imageAssetProcessor = services.GetRequiredService<IImageAssetProcessor>();
                var markdownProcessor = new MarkdownProcessor(options, imageAssetProcessor, contentOptions.Processing);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    options.ContentsPath,
                    (content, cancellationToken) => content.Body is { } body
                        ? markdownProcessor.ProcessBodyAsync(content.FileInfo.FilePath, body, cancellationToken)
                        : markdownProcessor.ProcessAsync(content.FileInfo.FilePath, cancellationToken),
                    CreateFrontMatterDeserializer,
                    sourceCache,
                    services.GetService<ContentFileHashRegistry>(),
                    contentOptions.ResolveContentsDirectory(options.ContentsPath),
                    contentOptions.Where)
                    .Build();

                // The projection is positional, so provenance is stated here rather than
                // derived: TModel need not expose its source file for a keyed lookup to
                // stay a single-file dependency.
                var items = new TModel[sources.Count];
                var provenance = new string?[sources.Count];
                for (var i = 0; i < sources.Count; i++)
                {
                    items[i] = select(sources[i]);
                    provenance[i] = sources[i].FileInfo.FilePath;
                }

                return new ContentSourceItems<TModel>(items, provenance);
            },
            key,
            contentOptions,
            contentOptions.ResolveContentSetScope());
    }
}
