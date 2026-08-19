using Kiji.Generation;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Reads a markdown file once and derives everything the build needs from that
/// single read: front matter, body, and the incremental-build content hash. This
/// replaces three independent reads of the same file (front matter parse, content
/// set fingerprint, render).
/// </summary>
internal static class MarkdownSourceReader
{
    internal static MarkdownSource<TFrontMatter> Read<TFrontMatter>(string filePath, IDeserializer deserializer)
    {
        var info = new FileInfo(filePath);
        return Read<TFrontMatter>(filePath, new MarkdownFileStamp(info.Length, info.LastWriteTimeUtc), deserializer);
    }

    /// <param name="stamp">
    /// The file's size and last write time from the directory walk that found it. It is
    /// recorded alongside the content hash, so a later build can trust the hash while
    /// the stamp still holds without opening the file.
    /// </param>
    internal static MarkdownSource<TFrontMatter> Read<TFrontMatter>(
        string filePath,
        MarkdownFileStamp stamp,
        IDeserializer deserializer)
    {
        var bytes = File.ReadAllBytes(filePath);
        var contentHash = BuildFingerprint.HashBytes(bytes);
        var content = DecodeText(bytes);

        TFrontMatter frontMatter;
        string body;
        try
        {
            frontMatter = MarkdownFrontMatterParser.ParseContent<TFrontMatter>(content, deserializer);
            body = MarkdownFrontMatterParser.RemoveFrontMatter(content);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to parse YAML front matter of '{filePath}'. {exception.Message}",
                exception);
        }

        return new MarkdownSource<TFrontMatter>(
            frontMatter,
            body,
            contentHash,
            stamp.Length,
            stamp.LastWriteTimeUtc);
    }

    private static string DecodeText(byte[] bytes)
    {
        // Matches File.ReadAllText semantics: UTF-8 by default with BOM detection.
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
