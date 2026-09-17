using Kiji.Markdown;
using Kiji.Generation;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Kiji.Benchmarks.MarkdownCacheCandidate;

/// <summary>Only syntax is cached. User conversions run on each materialization.</summary>
internal sealed record MarkdownParsedSource(
    string Body,
    byte[] Events,
    string ContentHash,
    (long Length, DateTime LastWriteTimeUtc) Stamp)
{
    private ParsingEvent[]? _firstEvents;

    internal void SetFirstEvents(ParsingEvent[] events) => _firstEvents = events;

    internal static MarkdownParsedSource Read(string path, (long Length, DateTime LastWriteTimeUtc) stamp)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        if (!MarkdownFrontMatterParser.TryExtractFrontMatter(text, out var yaml, out var body))
        {
            throw new InvalidOperationException("YAML front matter not found.");
        }
        var events = YamlEventCodec.ParseEvents(text[yaml]);
        var source = new MarkdownParsedSource(text[body], YamlEventCodec.Encode(events), BuildFingerprint.HashBytes(bytes), stamp);
        source.SetFirstEvents(events);
        return source;
    }

    internal MarkdownSource<T> Materialize<T>(IDeserializer deserializer)
    {
        var events = Interlocked.Exchange(ref _firstEvents, null) ?? YamlEventCodec.Decode(Events);
        var frontMatter = deserializer.Deserialize<T>(new YamlEventParser(events))
            ?? throw new InvalidOperationException("Failed to deserialize YAML front matter.");
        return new MarkdownSource<T>(frontMatter, Body, ContentHash);
    }
}
