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
    Func<FileInfo, bool>? filter = null)
{
    private readonly Func<IDeserializer> _frontMatterDeserializerFactory =
        frontMatterDeserializerFactory ?? (static () => new MarkdownOptions().CreateDeserializer());

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        if (!Directory.Exists(contentsDirectory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {contentsDirectory}");
        }

        var markdownFiles = FindMarkdownFiles(new DirectoryInfo(contentsDirectory));

        // Files are read and parsed in parallel; results land at their enumeration
        // index so the collection order stays deterministic. YamlDotNet deserializers
        // are not documented as thread-safe, so each worker uses its own instance.
        // Each file is read exactly once: the same read yields front matter, the body
        // used for rendering, and the content hash the incremental planner needs.
        var items = new MarkdownContent<TFrontMatter>[markdownFiles.Length];
        var errors = new Exception?[markdownFiles.Length];
        using var deserializers = new ThreadLocal<IDeserializer>(_frontMatterDeserializerFactory);

        Parallel.For(0, markdownFiles.Length, index =>
        {
            try
            {
                var fileInfo = markdownFiles[index];
                if (filter?.Invoke(fileInfo) == false)
                {
                    return;
                }

                var source = sourceCache is not null
                    ? sourceCache.GetOrRead(fileInfo, deserializers.Value!)
                    : MarkdownSourceReader.Read<TFrontMatter>(fileInfo, deserializers.Value!);
                items[index] = new MarkdownContent<TFrontMatter>(fileInfo, source.FrontMatter, source.Body, renderAsync, source.ContentHash,
                    Path.GetRelativePath(contentsDirectory, fileInfo.FullName).Replace('\\', '/'), source.FrontMatterDigest);
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
        return filter is null
            ? items
            : [.. items.Where(static item => item is not null)];
    }

    /// <summary>
    /// Lists Markdown files as a recursive <c>*.md</c> enumeration does, ordered by path.
    /// Content directories often hold one directory per post, so their subtrees are
    /// enumerated in parallel instead of opening every directory serially.
    /// </summary>
    private static FileInfo[] FindMarkdownFiles(DirectoryInfo root)
    {
        var subdirectories = root.EnumerateDirectories().ToArray();
        var found = new FileInfo[subdirectories.Length + 1][];
        found[0] = [.. root.EnumerateFiles("*.md")];
        var errors = new Exception?[subdirectories.Length];
        Parallel.For(0, subdirectories.Length, index =>
        {
            try
            {
                found[index + 1] = [.. subdirectories[index].EnumerateFiles("*.md", SearchOption.AllDirectories)];
            }
            catch (Exception exception)
            {
                errors[index] = exception;
            }
        });

        var firstError = Array.Find(errors, static error => error is not null);
        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        return [.. found.SelectMany(static files => files)
            .OrderBy(static file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static file => file.FullName, StringComparer.Ordinal)];
    }
}
