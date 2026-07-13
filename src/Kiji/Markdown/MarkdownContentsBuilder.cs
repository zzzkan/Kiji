using System.Runtime.ExceptionServices;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

public sealed class MarkdownContentsBuilder<TFrontMatter>(
    string contentsDirectory,
    Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
    Func<IDeserializer>? frontMatterDeserializerFactory = null)
{
    private readonly string _contentsDirectory = contentsDirectory;
    private readonly Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> _renderAsync = renderAsync;
    private readonly Func<IDeserializer> _frontMatterDeserializerFactory =
        frontMatterDeserializerFactory ?? (static () => MarkdownFrontMatterParser.DefaultDeserializer);
    private readonly MarkdownFrontMatterCache<TFrontMatter>? _frontMatterCache;

    internal MarkdownContentsBuilder(
        string contentsDirectory,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
        Func<IDeserializer> frontMatterDeserializerFactory,
        MarkdownFrontMatterCache<TFrontMatter> frontMatterCache)
        : this(contentsDirectory, renderAsync, frontMatterDeserializerFactory)
    {
        _frontMatterCache = frontMatterCache;
    }

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        if (!Directory.Exists(_contentsDirectory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {_contentsDirectory}");
        }

        var markdownFiles = Directory.EnumerateFiles(_contentsDirectory, "*.md", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Files are read and parsed in parallel; results land at their enumeration
        // index so the collection order stays deterministic. YamlDotNet deserializers
        // are not documented as thread-safe, so each worker uses its own instance.
        var items = new MarkdownContent<TFrontMatter>[markdownFiles.Length];
        var errors = new Exception?[markdownFiles.Length];
        using var deserializers = new ThreadLocal<IDeserializer>(_frontMatterDeserializerFactory);

        Parallel.For(0, markdownFiles.Length, index =>
        {
            try
            {
                var markdownFile = markdownFiles[index];
                var fileInfo = MarkdownFileInfo.Create(_contentsDirectory, markdownFile);
                var frontMatter = _frontMatterCache is not null
                    ? _frontMatterCache.GetOrParse(markdownFile, deserializers.Value!)
                    : MarkdownFrontMatterParser.Parse<TFrontMatter>(markdownFile, deserializers.Value!);
                items[index] = new MarkdownContent<TFrontMatter>(fileInfo, frontMatter, _renderAsync);
            }
            catch (Exception exception)
            {
                errors[index] = exception;
            }
        });

        // Surface the first failing file (in collection order) unwrapped, matching
        // the sequential behavior instead of an AggregateException.
        var firstError = Array.Find(errors, static error => error is not null);
        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        _frontMatterCache?.Prune(markdownFiles.ToHashSet(StringComparer.OrdinalIgnoreCase));

        return items;
    }
}
