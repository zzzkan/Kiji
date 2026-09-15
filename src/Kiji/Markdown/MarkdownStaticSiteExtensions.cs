using Kiji.Assets;
using Kiji.Generation;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Markdown content support for <see cref="StaticSite"/>.
/// </summary>
public static class MarkdownStaticSiteExtensions
{
    /// <summary>Uses Markdown files as a content dictionary with parsed front matter and lazily rendered bodies.</summary>
    /// <param name="key">Returns a non-empty, case-insensitively unique key; <see cref="MarkdownFileInfo.Slug"/> is a typical choice.</param>
    public static StaticSite UseMarkdownContent<TFrontMatter>(
        this StaticSite app,
        Func<MarkdownContent<TFrontMatter>, string> key,
        Action<MarkdownContentOptions<MarkdownContent<TFrontMatter>>>? configure = null)
        where TFrontMatter : class
    {
        return app.UseMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            key,
            configure);
    }

    /// <summary>Uses Markdown files projected into a dictionary of custom models.</summary>
    /// <param name="select">Projects each parsed file independently; the result must not depend on other items.</param>
    /// <param name="key">Returns a non-empty, case-insensitively unique key.</param>
    public static StaticSite UseMarkdownContent<TFrontMatter, TModel>(
        this StaticSite app,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Func<TModel, string> key,
        Action<MarkdownContentOptions<TModel>>? configure = null)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(app);
        app.EnsureConfigurable();
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

        return app.UseContentSource(
            services =>
            {
                var options = services.GetRequiredService<ResolvedSitePaths>();
                var imageAssetProcessor = services.GetRequiredService<IImageAssetProcessor>();
                var markdownProcessor = new MarkdownProcessor(options, imageAssetProcessor, contentOptions.Processing);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    options.ContentDirectory,
                    (content, cancellationToken) => content.Body is { } body
                        ? markdownProcessor.ProcessBodyAsync(content.FileInfo.FilePath, body, cancellationToken)
                        : markdownProcessor.ProcessAsync(content.FileInfo.FilePath, cancellationToken),
                    CreateFrontMatterDeserializer,
                    sourceCache,
                    services.GetService<ContentFileRegistry>(),
                    contentOptions.ResolveContentsDirectory(options.ContentDirectory),
                    contentOptions.FileFilter)
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
