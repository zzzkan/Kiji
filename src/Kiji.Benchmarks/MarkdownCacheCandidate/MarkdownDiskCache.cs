using Kiji.Markdown;
using System.Runtime.InteropServices;
using System.Text;
using Kiji.Generation;
using YamlDotNet.Core;

namespace Kiji.Benchmarks.MarkdownCacheCandidate;

/// <summary>One atomic snapshot per registration; cache failures never hide source failures.</summary>
internal static class MarkdownDiskCache
{
    private const int Schema = 1;
    private static readonly string Identity = $"{typeof(MarkdownDiskCache).Assembly.ManifestModule.ModuleVersionId:N}:" +
        $"{typeof(Parser).Assembly.ManifestModule.ModuleVersionId:N}:{RuntimeInformation.FrameworkDescription}:{RuntimeInformation.ProcessArchitecture}";

    internal static Dictionary<string, MarkdownParsedSource> Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != Schema || reader.ReadString() != Identity) { return []; }
            var length = reader.ReadInt32();
            var hash = reader.ReadString();
            if (length < 0 || length != stream.Length - stream.Position) { return []; }
            var payload = reader.ReadBytes(length);
            if (BuildFingerprint.HashBytes(payload) != hash) { return []; }
            using var data = new BinaryReader(new MemoryStream(payload, writable: false), Encoding.UTF8);
            var result = new Dictionary<string, MarkdownParsedSource>(StringComparer.OrdinalIgnoreCase);
            while (data.BaseStream.Position < data.BaseStream.Length)
            {
                var key = data.ReadString();
                if (Path.IsPathFullyQualified(key) || key.Split('/', '\\').Any(static part => part is ".." or "."))
                {
                    return [];
                }
                var stamp = new MarkdownFileStamp(data.ReadInt64(), new DateTime(data.ReadInt64(), DateTimeKind.Utc));
                var contentHash = data.ReadString();
                var body = data.ReadString();
                var eventLength = data.ReadInt32();
                if (stamp.Length < 0 || eventLength < 0 || eventLength > data.BaseStream.Length - data.BaseStream.Position)
                {
                    return [];
                }
                var events = data.ReadBytes(eventLength);
                // Decode before any user converter, so cache errors cannot cause
                // a converter to run twice or hide its exception.
                var source = new MarkdownParsedSource(body, events, contentHash, stamp);
                result.Add(key, source);
            }
            // Validation has no user callbacks and is independent per record.
            // Restore in parallel, just like the normal content-read path.
            Parallel.ForEach(result.Values, static source => source.SetFirstEvents(YamlEventCodec.Decode(source.Events)));
            return result;
        }
        catch (Exception e) when (IsCacheFailure(e)) { return []; }
    }

    internal static void Save(string path, IReadOnlyDictionary<string, MarkdownParsedSource> entries)
    {
        string? temporary = null;
        try
        {
            using var payload = new MemoryStream();
            using (var data = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var (key, source) in entries.OrderBy(static x => x.Key, StringComparer.Ordinal))
                {
                    data.Write(key); data.Write(source.Stamp.Length); data.Write(source.Stamp.LastWriteTimeUtc.Ticks);
                    data.Write(source.ContentHash); data.Write(source.Body);
                    data.Write(source.Events.Length); data.Write(source.Events);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            using (var file = File.Create(temporary))
            using (var writer = new BinaryWriter(file, Encoding.UTF8))
            {
                var bytes = payload.GetBuffer().AsSpan(0, checked((int)payload.Length));
                writer.Write(Schema); writer.Write(Identity); writer.Write(bytes.Length);
                writer.Write(BuildFingerprint.HashBytes(bytes)); writer.Write(bytes);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (IsCacheFailure(e)) { /* Optional cache; source remains authoritative. */ }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception e) when (IsCacheFailure(e)) { }
            }
        }
    }

    private static bool IsCacheFailure(Exception e) => e is IOException or UnauthorizedAccessException
        or ArgumentException or FormatException or OverflowException or YamlException
        || (e is AggregateException aggregate && aggregate.InnerExceptions.All(IsCacheFailure));
}
