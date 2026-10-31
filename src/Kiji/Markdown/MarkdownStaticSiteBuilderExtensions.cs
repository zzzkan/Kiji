using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kiji.Markdown;

/// <summary>
/// Markdown content support for <see cref="StaticSiteBuilder"/>.
/// </summary>
public static class MarkdownStaticSiteBuilderExtensions
{
    /// <summary>Adds Markdown files as a content collection with parsed front matter and lazily rendered bodies.</summary>
    public static StaticSiteBuilder AddMarkdownContent<TFrontMatter>(
        this StaticSiteBuilder builder,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
    {
        return builder.AddMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            configure);
    }

    /// <summary>Adds Markdown files projected into a collection of custom models.</summary>
    /// <param name="select">Projects each parsed file independently; the result must not depend on other items.</param>
    public static StaticSiteBuilder AddMarkdownContent<TFrontMatter, TModel>(
        this StaticSiteBuilder builder,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        where TModel : class
        => AddMarkdownContentCore(builder, null, select, configure);

    /// <summary>Adds a keyed collection of Markdown files with parsed front matter and lazily rendered bodies.</summary>
    public static StaticSiteBuilder AddKeyedMarkdownContent<TFrontMatter>(
        this StaticSiteBuilder builder,
        string serviceKey,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        => builder.AddKeyedMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            serviceKey, static content => content, configure);

    /// <summary>Adds Markdown files projected into a keyed collection of custom models.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="serviceKey">A non-empty service key identifying this collection.</param>
    /// <param name="select">Projects each parsed file independently; the result must not depend on other items.</param>
    /// <param name="configure">Selects the source directory and files.</param>
    public static StaticSiteBuilder AddKeyedMarkdownContent<TFrontMatter, TModel>(
        this StaticSiteBuilder builder,
        string serviceKey,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);
        return AddMarkdownContentCore(builder, serviceKey, select, configure);
    }

    private static StaticSiteBuilder AddMarkdownContentCore<TFrontMatter, TModel>(
        StaticSiteBuilder builder,
        string? serviceKey,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(select);

        var contentOptions = new MarkdownContentOptions();
        configure?.Invoke(contentOptions);
        var directory = contentOptions.Directory;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // Created once per registration so it survives content re-materializations
        // in the dev server; fresh hashes let it re-parse only changed files.
        var sourceCache = new MarkdownSourceCache<TFrontMatter>();

        return builder.RegisterContent(
            (services, sourceId) =>
            {
                var options = services.GetRequiredService<IOptions<MarkdownOptions>>().Value;
                var markdownProcessor = services.GetRequiredService<MarkdownProcessor>();
                var sourceDirectory = builder.Paths.ResolveAgainstRoot(directory);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    sourceDirectory,
                    (content, cancellationToken) => markdownProcessor.ProcessBodyAsync(
                        content.SourcePath,
                        content.Body,
                        cancellationToken),
                    options.CreateDeserializer,
                    sourceCache,
                    builder.Runtime.Files,
                    contentOptions.FileFilter)
                    .Build();

                // Item lookups retain the parsed-byte hash even when the projection
                // hides its source. Collection readers usually depend only on metadata.
                var items = new (string Id, TModel Item, string? Digest, string? ProjectionDigest, string? FrontMatterDigest)[sources.Count];
                for (var i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    source.TrackFrontMatter(sourceId);
                    var item = select(source);
                    items[i] = (source.Path, item, source.ContentHash,
                        source.WasRendered ? source.ContentHash : source.FrontMatterDigest, source.FrontMatterDigest);
                }

                return items;
            },
            directory: directory,
            serviceKey: serviceKey);
    }
}
