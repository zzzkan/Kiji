using System.Runtime.ExceptionServices;
using Kiji.Generation;
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
    private readonly MarkdownSourceCache<TFrontMatter>? _sourceCache;
    private readonly ContentFileRegistry? _hashRegistry;

    /// <summary>
    /// The directory actually scanned. Defaults to the content directory; a source
    /// reading a subdirectory narrows it. Paths on <see cref="MarkdownFileInfo"/> stay
    /// relative to <see cref="_contentsDirectory"/> either way, so narrowing the scan
    /// never changes what an item reports about itself.
    /// </summary>
    private readonly string? _scanDirectory;

    private readonly Func<MarkdownFileInfo, bool>? _filter;

    internal MarkdownContentsBuilder(
        string contentsDirectory,
        Func<MarkdownContent<TFrontMatter>, CancellationToken, Task<string>> renderAsync,
        Func<IDeserializer> frontMatterDeserializerFactory,
        MarkdownSourceCache<TFrontMatter> sourceCache,
        ContentFileRegistry? hashRegistry = null,
        string? scanDirectory = null,
        Func<MarkdownFileInfo, bool>? filter = null)
        : this(contentsDirectory, renderAsync, frontMatterDeserializerFactory)
    {
        _sourceCache = sourceCache;
        _hashRegistry = hashRegistry;
        _scanDirectory = scanDirectory;
        _filter = filter;
    }

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        var scanDirectory = _scanDirectory ?? _contentsDirectory;
        if (!Directory.Exists(scanDirectory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {scanDirectory}");
        }

        // Enumerating FileInfo rather than paths: the directory walk already carries
        // each entry's size and last write time, so nothing here has to go back to the
        // filesystem for them (28 ms against 54 ms over a thousand files,
        // Kiji.Benchmarks DirectoryScanBenchmarks).
        var markdownFiles = new DirectoryInfo(scanDirectory)
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
        var included = _filter is null ? null : new bool[markdownFiles.Length];
        using var deserializers = new ThreadLocal<IDeserializer>(_frontMatterDeserializerFactory);

        Parallel.For(0, markdownFiles.Length, index =>
        {
            try
            {
                var markdownFile = markdownFiles[index].FullName;
                var stamp = MarkdownFileStamp.From(markdownFiles[index]);
                var fileInfo = MarkdownFileInfo.Create(_contentsDirectory, markdownFile, stamp);
                if (_filter is not null)
                {
                    if (!_filter(fileInfo))
                    {
                        return;
                    }

                    included![index] = true;
                }

                var source = _sourceCache is not null
                    ? _sourceCache.GetOrRead(markdownFile, stamp, deserializers.Value!)
                    : MarkdownSourceReader.Read<TFrontMatter>(markdownFile, stamp, deserializers.Value!);
                items[index] = new MarkdownContent<TFrontMatter>(fileInfo, source.FrontMatter, source.Body, _renderAsync);
                _hashRegistry?.Record(markdownFile, source.Length, source.LastWriteTimeUtc, source.ContentHash);
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

        _sourceCache?.Prune(markdownFiles.Select(static file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // The planner fingerprints the same tree; handing it this listing spares it a
        // second walk, which is the largest single cost left in a no-change build.
        _hashRegistry?.RecordScan(
            scanDirectory,
            [.. markdownFiles.Select(static file => new ScannedFile(file.FullName, file.Length, file.LastWriteTimeUtc))]);

        // Filtered-out slots were never assigned; compacting keeps the order above.
        return included is null
            ? items
            : [.. items.Where((_, index) => included[index])];
    }
}
