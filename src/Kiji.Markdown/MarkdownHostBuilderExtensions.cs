using Kiji.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kiji.Markdown;

/// <summary>Adds Markdown content to a site.</summary>
public static class MarkdownHostBuilderExtensions
{
    /// <summary>Adds a <see cref="ContentCollection{T}"/> of Markdown files.</summary>
    public static IHostApplicationBuilder AddMarkdownContent<TFrontMatter>(
        this IHostApplicationBuilder builder,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
    {
        return builder.AddMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            configure);
    }

    /// <summary>Adds a <see cref="ContentCollection{T}"/> of models made from Markdown files.</summary>
    /// <param name="select">Makes one model from one file, without reading other items.</param>
    public static IHostApplicationBuilder AddMarkdownContent<TFrontMatter, TModel>(
        this IHostApplicationBuilder builder,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        where TModel : class
        => AddMarkdownContentCore(builder, null, select, configure);

    /// <summary>Adds a keyed <see cref="ContentCollection{T}"/> of Markdown files.</summary>
    public static IHostApplicationBuilder AddKeyedMarkdownContent<TFrontMatter>(
        this IHostApplicationBuilder builder,
        string serviceKey,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        => builder.AddKeyedMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            serviceKey, static content => content, configure);

    /// <summary>Adds a keyed <see cref="ContentCollection{T}"/> of models made from Markdown files.</summary>
    /// <param name="builder">The site builder.</param>
    /// <param name="serviceKey">The collection's non-empty service key.</param>
    /// <param name="select">Makes one model from one file, without reading other items.</param>
    /// <param name="configure">Selects the directory and files.</param>
    public static IHostApplicationBuilder AddKeyedMarkdownContent<TFrontMatter, TModel>(
        this IHostApplicationBuilder builder,
        string serviceKey,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);
        return AddMarkdownContentCore(builder, serviceKey, select, configure);
    }

    private static IHostApplicationBuilder AddMarkdownContentCore<TFrontMatter, TModel>(
        IHostApplicationBuilder builder,
        string? serviceKey,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        var context = SiteBuilderContext.Get(builder);
        ArgumentNullException.ThrowIfNull(select);
        MarkdownWarmup.Start();

        var contentOptions = new MarkdownContentOptions();
        configure?.Invoke(contentOptions);
        var directory = contentOptions.Directory;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var sourceCache = new MarkdownSourceCache<TFrontMatter>();

        return builder.RegisterContent(
            (services, sourceId) =>
            {
                var options = services.GetRequiredService<IOptions<MarkdownOptions>>().Value;
                var markdownProcessor = services.GetRequiredService<MarkdownProcessor>();
                var sourceDirectory = context.Paths.ResolveAgainstRoot(directory);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    sourceDirectory,
                    (content, cancellationToken) => markdownProcessor.ProcessBodyAsync(
                        content.SourcePath,
                        content.Body,
                        cancellationToken),
                    options.CreateDeserializer,
                    sourceCache,
                    context.Runtime.Files,
                    contentOptions.FileFilter)
                    .Build();

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
