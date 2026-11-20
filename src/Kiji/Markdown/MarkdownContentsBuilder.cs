using System.IO.Enumeration;
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

    private static readonly string MarkdownPattern = FileSystemName.TranslateWin32Expression("*.md");

    private static readonly bool IgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();

    public IReadOnlyList<MarkdownContent<TFrontMatter>> Build()
    {
        if (!Directory.Exists(contentsDirectory))
        {
            throw new DirectoryNotFoundException($"Contents directory not found: {contentsDirectory}");
        }

        var markdownFiles = FindMarkdownFiles(new DirectoryInfo(contentsDirectory));

        var included = filter is null ? null : Array.ConvertAll(markdownFiles, file => filter(file));
        var items = new MarkdownContent<TFrontMatter>[markdownFiles.Length];
        var errors = new Exception?[markdownFiles.Length];
        using var deserializers = new ThreadLocal<IDeserializer>(_frontMatterDeserializerFactory);

        Parallel.For(0, markdownFiles.Length, index =>
        {
            try
            {
                var fileInfo = markdownFiles[index];
                if (included?[index] == false)
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

        var firstError = Array.Find(errors, static error => error is not null);
        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        var ids = new Dictionary<string, MarkdownContent<TFrontMatter>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item is not null && !ids.TryAdd(item.Path, item))
            {
                throw new InvalidOperationException(
                    $"Markdown files '{ids[item.Path].SourcePath}' and '{item.SourcePath}' share the content ID '{item.Path}'. "
                    + "IDs ignore case and use '/' separators; rename one of the files.");
            }
        }

        sourceCache?.Prune(markdownFiles.Select(static file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        return filter is null
            ? items
            : [.. items.Where(static item => item is not null)];
    }

    /// <summary>
    /// Lists Markdown files as a recursive <c>*.md</c> enumeration does, ordered by path.
    /// Content directories often hold one directory per post, so their subtrees are
    /// enumerated in parallel instead of opening every directory serially.
    /// </summary>
    /// <remarks>
    /// The root may itself be a link, as for watching and image resolution. Redirecting
    /// directories and Markdown files below it are rejected rather than followed, so
    /// content cannot read outside its registered root or loop.
    /// </remarks>
    private static FileInfo[] FindMarkdownFiles(DirectoryInfo root)
    {
        var subdirectories = root.EnumerateDirectories().ToArray();
        var found = new FileInfo[subdirectories.Length + 1][];
        found[0] = [.. EnumerateMarkdownFiles(root.FullName, recurse: false)];
        var errors = new Exception?[subdirectories.Length];
        Parallel.For(0, subdirectories.Length, index =>
        {
            try
            {
                RejectLink(subdirectories[index]);
                found[index + 1] = [.. EnumerateMarkdownFiles(subdirectories[index].FullName, recurse: true)];
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

    private static FileSystemEnumerable<FileInfo> EnumerateMarkdownFiles(string directory, bool recurse)
    {
        return new FileSystemEnumerable<FileInfo>(
            directory,
            static (ref entry) => (FileInfo)entry.ToFileSystemInfo(),
            new EnumerationOptions
            {
                RecurseSubdirectories = recurse,
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
                MatchType = MatchType.Win32,
            })
        {
            ShouldIncludePredicate = static (ref entry) =>
            {
                if (entry.IsDirectory || !FileSystemName.MatchesWin32Expression(MarkdownPattern, entry.FileName, IgnoreCase))
                {
                    return false;
                }
                RejectLink(ref entry);
                return true;
            },
            ShouldRecursePredicate = static (ref entry) =>
            {
                RejectLink(ref entry);
                return true;
            },
        };
    }

    private static void RejectLink(ref FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { RejectLink(entry.ToFileSystemInfo()); }
    }

    private static void RejectLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 && entry.LinkTarget is not null)
        {
            throw new InvalidOperationException(
                $"Markdown content '{entry.FullName}' cannot be loaded through symbolic links or junctions below a content root.");
        }
    }
}
