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
    public static StaticSite UseMarkdownContent<TFrontMatter>(
        this StaticSite app,
        Action<MarkdownOptions>? configure = null)
        where TFrontMatter : class
    {
        return app.UseMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            configure);
    }

    /// <summary>Uses Markdown files projected into a dictionary of custom models.</summary>
    /// <param name="select">Projects each parsed file independently; the result must not depend on other items.</param>
    public static StaticSite UseMarkdownContent<TFrontMatter, TModel>(
        this StaticSite app,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownOptions>? configure = null)
        where TFrontMatter : class
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(app);
        app.EnsureConfigurable();
        ArgumentNullException.ThrowIfNull(select);

        var contentOptions = new MarkdownOptions();
        configure?.Invoke(contentOptions);

        // A factory rather than a shared instance: front matter parsing runs on
        // multiple threads and YamlDotNet deserializers are not documented as thread-safe.
        IDeserializer CreateFrontMatterDeserializer()
        {
            return MarkdownFrontMatterParser.CreateDeserializer(contentOptions.FrontMatterConfigurations);
        }

        // Created once per registration so it survives content re-materializations
        // in the dev server; fresh hashes let it re-parse only changed files.
        var sourceCache = new MarkdownSourceCache<TFrontMatter>();

        return app.UseContentSource(
            services =>
            {
                var options = services.GetRequiredService<ResolvedSitePaths>();
                var imageProcessor = services.GetRequiredService<IImageProcessor>();
                var markdownProcessor = new MarkdownProcessor(options, imageProcessor, contentOptions.Processing);
                var sourceDirectory = contentOptions.ResolveContentsDirectory(options.ContentDirectory);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    options.ContentDirectory,
                    (content, cancellationToken) => markdownProcessor.ProcessBodyAsync(
                        content.FileInfo.FullName,
                        content.Body,
                        cancellationToken),
                    CreateFrontMatterDeserializer,
                    sourceCache,
                    services.GetService<ContentFileRegistry>(),
                    sourceDirectory,
                    contentOptions.FileFilter)
                    .Build();

                // The projection is positional, so provenance is stated here rather than
                // derived: TModel need not expose its source file for a keyed lookup to
                // stay a single-file dependency.
                var items = new (string Key, TModel Item, string? SourceFile)[sources.Count];
                for (var i = 0; i < sources.Count; i++)
                {
                    items[i] = (Path.GetRelativePath(sourceDirectory, sources[i].FileInfo.FullName).Replace('\\', '/'), select(sources[i]), sources[i].FileInfo.FullName);
                }

                return items;
            },
            contentOptions.ResolveContentSetScope());
    }
}
