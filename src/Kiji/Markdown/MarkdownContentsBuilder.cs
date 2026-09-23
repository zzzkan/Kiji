using System.Runtime.ExceptionServices;
using Kiji.Generation;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

internal sealed class MarkdownContentsBuilder<TFrontMatter>(
    string contentsDirectory,
    Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
    Func<IDeserializer>? frontMatterDeserializerFactory = null,
    MarkdownSourceCache<TFrontMatter>? sourceCache = null,
    ContentFileRegistry? hashRegistry = null,
    string? scanDirectory = null,
    Func<FileInfo, bool>? filter = null)
{
    private readonly Func<IDeserializer> _frontMatterDeserializerFactory =
        frontMatterDeserializerFactory ?? (static () => MarkdownFrontMatterParser.CreateDeserializer(null));

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        var directory = scanDirectory ?? contentsDirectory;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {directory}");
        }

        var markdownFiles = new DirectoryInfo(directory)
            .EnumerateFiles("*.md", SearchOption.AllDirectories)
            .OrderBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Files are read and parsed in parallel; results land at their enumeration
        // index so the collection order stays deterministic. YamlDotNet deserializers
        // are not documented as thread-safe, so each worker uses its own instance.
        // Each file is read exactly once: the same read yields front matter, the body
        // used for rendering, and the content hash the incremental planner needs.
        var items = new MarkdownContent<TFrontMatter>[markdownFiles.Length];
        var errors = new Exception?[markdownFiles.Length];
        // Only allocated when filtering, so the common path keeps the array as-is.
        var included = filter is null ? null : new bool[markdownFiles.Length];
        using var deserializers = new ThreadLocal<IDeserializer>(_frontMatterDeserializerFactory);

        Parallel.For(0, markdownFiles.Length, index =>
        {
            try
            {
                var fileInfo = markdownFiles[index];
                if (filter is not null)
                {
                    if (!filter(fileInfo))
                    {
                        return;
                    }

                    included![index] = true;
                }

                var source = sourceCache is not null
                    ? sourceCache.GetOrRead(fileInfo, deserializers.Value!)
                    : MarkdownSourceReader.Read<TFrontMatter>(fileInfo, deserializers.Value!);
                items[index] = new MarkdownContent<TFrontMatter>(fileInfo, source.FrontMatter, source.Body, renderAsync, source.ContentHash);
                hashRegistry?.Record(fileInfo, source.ContentHash);
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

        sourceCache?.Prune(markdownFiles.Select(static file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // Filtered-out slots were never assigned; compacting keeps the order above.
        return included is null
            ? items
            : [.. items.Where((_, index) => included[index])];
    }
}
