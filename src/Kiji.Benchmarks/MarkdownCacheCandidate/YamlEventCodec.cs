using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Core.Tokens;
using AnchorAlias = YamlDotNet.Core.Events.AnchorAlias;
using Comment = YamlDotNet.Core.Events.Comment;
using DocumentEnd = YamlDotNet.Core.Events.DocumentEnd;
using DocumentStart = YamlDotNet.Core.Events.DocumentStart;
using Scalar = YamlDotNet.Core.Events.Scalar;
using StreamStart = YamlDotNet.Core.Events.StreamStart;
using StreamEnd = YamlDotNet.Core.Events.StreamEnd;

namespace Kiji.Benchmarks.MarkdownCacheCandidate;

/// <summary>A versioned, lossless encoding of the public YAML parser events.</summary>
internal static class YamlEventCodec
{
    internal static byte[] Parse(string yaml)
    {
        return Encode(ParseEvents(yaml));
    }

    internal static ParsingEvent[] ParseEvents(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        var events = new List<ParsingEvent>();
        while (parser.MoveNext()) { events.Add(parser.Current!); }
        return [.. events];
    }

    internal static byte[] Encode(IEnumerable<ParsingEvent> events)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (var e in events)
        {
            writer.Write((byte)(e switch
            {
                StreamStart => 1,
                StreamEnd => 2,
                DocumentStart => 3,
                DocumentEnd => 4,
                Scalar => 5,
                SequenceStart => 6,
                SequenceEnd => 7,
                MappingStart => 8,
                MappingEnd => 9,
                AnchorAlias => 10,
                Comment => 11,
                _ => throw new InvalidDataException("Unknown YAML event."),
            }));
            WriteMark(writer, e.Start);
            WriteMark(writer, e.End);
            if (e is NodeEvent node)
            {
                writer.Write(node.Anchor.IsEmpty ? "" : node.Anchor.Value);
                writer.Write(node.Tag.IsEmpty ? "" : node.Tag.Value);
            }
            switch (e)
            {
                case Scalar scalar:
                    writer.Write(scalar.Value);
                    writer.Write((int)scalar.Style);
                    writer.Write(scalar.IsPlainImplicit);
                    writer.Write(scalar.IsQuotedImplicit);
                    writer.Write(scalar.IsKey);
                    break;
                case SequenceStart sequence:
                    writer.Write(sequence.IsImplicit); writer.Write((int)sequence.Style); break;
                case MappingStart mapping:
                    writer.Write(mapping.IsImplicit); writer.Write((int)mapping.Style); break;
                case AnchorAlias alias: writer.Write(alias.Value.Value); break;
                case Comment comment: writer.Write(comment.Value); writer.Write(comment.IsInline); break;
                case DocumentEnd end: writer.Write(end.IsImplicit); break;
                case DocumentStart start:
                    writer.Write(start.IsImplicit);
                    writer.Write(start.Version is not null);
                    if (start.Version is { } version)
                    {
                        writer.Write(version.Version.Major); writer.Write(version.Version.Minor);
                        WriteMark(writer, version.Start); WriteMark(writer, version.End);
                    }
                    writer.Write(start.Tags?.Count ?? -1);
                    foreach (var tag in start.Tags ?? [])
                    {
                        writer.Write(tag.Handle); writer.Write(tag.Prefix);
                        WriteMark(writer, tag.Start); WriteMark(writer, tag.End);
                    }
                    break;
            }
        }
        return stream.ToArray();
    }

    internal static ParsingEvent[] Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        var events = new List<ParsingEvent>();
        while (stream.Position < stream.Length)
        {
            var kind = reader.ReadByte();
            var start = ReadMark(reader);
            var end = ReadMark(reader);
            var anchor = AnchorName.Empty;
            var tag = TagName.Empty;
            if (kind is 5 or 6 or 8)
            {
                var a = reader.ReadString(); var t = reader.ReadString();
                anchor = a.Length == 0 ? AnchorName.Empty : new AnchorName(a);
                tag = t.Length == 0 ? TagName.Empty : new TagName(t);
            }
            events.Add(kind switch
            {
                1 => new StreamStart(start, end),
                2 => new StreamEnd(start, end),
                3 => ReadDocumentStart(reader, start, end),
                4 => new DocumentEnd(reader.ReadBoolean(), start, end),
                5 => new Scalar(anchor, tag, reader.ReadString(), ReadEnum<ScalarStyle>(reader),
                    reader.ReadBoolean(), reader.ReadBoolean(), start, end, reader.ReadBoolean()),
                6 => new SequenceStart(anchor, tag, reader.ReadBoolean(), ReadEnum<SequenceStyle>(reader), start, end),
                7 => new SequenceEnd(start, end),
                8 => new MappingStart(anchor, tag, reader.ReadBoolean(), ReadEnum<MappingStyle>(reader), start, end),
                9 => new MappingEnd(start, end),
                10 => new AnchorAlias(new AnchorName(reader.ReadString()), start, end),
                11 => new Comment(reader.ReadString(), reader.ReadBoolean(), start, end),
                _ => throw new InvalidDataException("Unknown YAML event."),
            });
        }
        if (events.Count < 2 || events[0] is not StreamStart || events[^1] is not StreamEnd)
        {
            throw new InvalidDataException("Incomplete YAML event stream.");
        }
        return [.. events];
    }

    private static DocumentStart ReadDocumentStart(BinaryReader reader, Mark start, Mark end)
    {
        var implicitStart = reader.ReadBoolean();
        VersionDirective? version = null;
        if (reader.ReadBoolean())
        {
            var value = new YamlDotNet.Core.Version(reader.ReadInt32(), reader.ReadInt32());
            version = new VersionDirective(value, ReadMark(reader), ReadMark(reader));
        }
        var count = reader.ReadInt32();
        TagDirectiveCollection? tags = count == -1 ? null : new();
        if (count < -1 || count > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("Invalid directive count.");
        }
        for (var i = 0; i < count; i++)
        {
            tags!.Add(new TagDirective(reader.ReadString(), reader.ReadString(), ReadMark(reader), ReadMark(reader)));
        }
        return new DocumentStart(version, tags, implicitStart, start, end);
    }

    private static T ReadEnum<T>(BinaryReader reader) where T : struct, Enum
    {
        var value = (T)Enum.ToObject(typeof(T), reader.ReadInt32());
        return Enum.IsDefined(value) ? value : throw new InvalidDataException("Unknown YAML style.");
    }

    private static void WriteMark(BinaryWriter writer, Mark mark)
    {
        writer.Write(mark.Index); writer.Write(mark.Line); writer.Write(mark.Column);
    }
    private static Mark ReadMark(BinaryReader reader) => new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
}
