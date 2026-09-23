using Kiji.Generation;
using System.Text;
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
    internal static MarkdownSource<TFrontMatter> Read<TFrontMatter>(
        FileInfo file,
        IDeserializer deserializer)
    {
        var bytes = File.ReadAllBytes(file.FullName);
        var contentHash = BuildFingerprint.HashBytes(bytes);
        var content = DecodeText(bytes);

        (TFrontMatter FrontMatter, string Body) parsed;
        try
        {
            parsed = MarkdownFrontMatterParser.ParseContentAndBody<TFrontMatter>(content, deserializer);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to parse YAML front matter of '{file.FullName}'. {exception.Message}",
                exception);
        }

        return new MarkdownSource<TFrontMatter>(
            parsed.FrontMatter,
            parsed.Body,
            contentHash);
    }

    internal static string DecodeText(byte[] bytes)
    {
        // UTF-8 needs no stream or intermediate decoder buffers. Keep StreamReader
        // for UTF-16/32 BOMs so its encoding detection and fallback remain intact.
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) { return Encoding.UTF8.GetString(bytes.AsSpan(3)); }
        if (bytes is not ([0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0, 0, 0xFE, 0xFF, ..]))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
