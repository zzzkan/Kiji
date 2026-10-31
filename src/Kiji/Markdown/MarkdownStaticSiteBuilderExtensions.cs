using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kiji.Markdown;

/// <summary>
/// Markdown content support for <see cref="StaticSiteBuilder"/>.
/// </summary>
public static class MarkdownStaticSiteBuilderExtensions
{
    /// <summary>Adds Markdown files as a content dictionary with parsed front matter and lazily rendered bodies.</summary>
    public static StaticSiteBuilder AddMarkdownContent<TFrontMatter>(
        this StaticSiteBuilder builder,
        Action<MarkdownContentOptions>? configure = null)
        where TFrontMatter : class
    {
        return builder.AddMarkdownContent<TFrontMatter, MarkdownContent<TFrontMatter>>(
            static content => content,
            configure);
    }

    /// <summary>Adds Markdown files projected into a dictionary of custom models.</summary>
    /// <param name="select">Projects each parsed file independently; the result must not depend on other items.</param>
    public static StaticSiteBuilder AddMarkdownContent<TFrontMatter, TModel>(
        this StaticSiteBuilder builder,
        Func<MarkdownContent<TFrontMatter>, TModel> select,
        Action<MarkdownContentOptions>? configure = null)
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
            services =>
            {
                var options = services.GetRequiredService<IOptions<MarkdownOptions>>().Value;
                var markdownProcessor = services.GetRequiredService<MarkdownProcessor>();
                var sourceDirectory = builder.Paths.ResolveAgainstRoot(directory);

                var sources = new MarkdownContentsBuilder<TFrontMatter>(
                    sourceDirectory,
                    (content, cancellationToken) => markdownProcessor.ProcessBodyAsync(
                        content.FileInfo.FullName,
                        content.Body,
                        cancellationToken),
                    options.CreateDeserializer,
                    sourceCache,
                    builder.Runtime.Files,
                    contentOptions.FileFilter)
                    .Build();

                // Keep the parsed-byte hash even when the projection hides its source.
                var items = new (string Key, TModel Item, string? Digest)[sources.Count];
                for (var i = 0; i < sources.Count; i++)
                {
                    items[i] = (sources[i].Path, select(sources[i]), sources[i].ContentHash);
                }

                return items;
            },
            directory: directory);
    }
}
