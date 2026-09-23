using System.Text.Json;
using Kiji.Generation;
using Kiji.Rendering;

namespace Kiji.Assets;

/// <summary>Kiji owns persistence; encoders only write to isolated working directories.</summary>
internal static class ImageArtifactProcessor
{
    internal static async Task<ProcessedImageInfo> ProcessAsync(IImageProcessor processor,
        string source, string output, string? cacheDirectory, CancellationToken cancellationToken)
    {
        if (cacheDirectory is null && PageRenderContext.Current?.Dependencies is null)
        {
            return await processor.ProcessAsync(source, output, cancellationToken);
        }
        var sourceBytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sourceHash = BuildFingerprint.HashBytes(sourceBytes);
        PageRenderContext.Current?.Dependencies?.AddFile(source, sourceHash);
        var identity = ImageProcessorIdentity.Get(processor);
        PageRenderContext.Current?.Dependencies?.AddValue("image-processor", identity);
        PageRenderContext.Current?.Dependencies?.AddImageRequest(source, output);
        if (cacheDirectory is null || identity is null)
        {
            PageRenderContext.Current?.Dependencies?.DisableCache();
            return await processor.ProcessAsync(source, output, cancellationToken);
        }
        var root = Path.GetDirectoryName(cacheDirectory)!;
        var cache = new ArtifactCache(root);
        var key = BuildFingerprint.HashText(identity + ":" + Path.GetFileName(source) + ":" + sourceHash);
        PageRenderContext.Current?.Dependencies?.AddImage(key);
        var recordPath = Path.Combine(root, "image-records", key + ".json");
        using var generation = await ImageGenerationLock.AcquireAsync(recordPath, cancellationToken);
        try
        {
            if (File.Exists(recordPath))
            {
                var envelope = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(recordPath, cancellationToken), ImageCacheJsonContext.Default.ImageCacheEnvelope);
                var record = envelope?.Payload is { } payload && BuildFingerprint.HashText(payload) == envelope.Digest
                    ? JsonSerializer.Deserialize(payload, ImageCacheJsonContext.Default.CachedImageResult)
                    : null;
                if (record is not null && record.Info is { OriginalWidth: > 0, OriginalHeight: > 0, Variants: not null } && record.Outputs is not null
                    && record.Info.Variants.All(v => v is not null && v.Width > 0 && BuildManifest.IsRelativeOutput(v.FileName))
                    && record.Outputs.All(o => o is not null)
                    && record.Info.Variants.Count == record.Outputs.Count
                    && record.Info.Variants.Select(v => v.FileName).SequenceEqual(record.Outputs.Select(o => o.RelativePath))
                    && record.Outputs.All(o => BuildManifest.IsRelativeOutput(o.RelativePath)
                        && ArtifactCache.IsHash(o.Hash))
                    && record.Outputs.All(o => cache.Restore(o.Hash, Path.Combine(output, o.RelativePath))))
                { return record.Info; }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        var work = Path.Combine(root, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var generated = Path.Combine(work, "output");
            Directory.CreateDirectory(generated);
            ProcessedImageInfo info;
            if (processor is ImageProcessor builtIn)
            {
                // The built-in encoder can consume the exact bytes already hashed.
                info = await builtIn.ProcessBytesAsync(sourceBytes, Path.GetFileName(source), sourceHash, generated, cancellationToken);
            }
            else
            {
                // Path-based custom encoders receive an isolated source snapshot.
                var sourceCopy = Path.Combine(work, "source", Path.GetFileName(source));
                Directory.CreateDirectory(Path.GetDirectoryName(sourceCopy)!);
                await File.WriteAllBytesAsync(sourceCopy, sourceBytes, cancellationToken);
                info = await processor.ProcessAsync(sourceCopy, generated, cancellationToken);
            }
            var outputs = info.Variants.Select(variant =>
            {
                return !BuildManifest.IsRelativeOutput(variant.FileName)
                    ? throw new InvalidDataException("Invalid image variant path.")
                    : new BuildManifestOutput(variant.FileName, cache.Store(Path.Combine(generated, variant.FileName)));
            }).ToArray();
            foreach (var artifact in outputs)
            {
                if (!cache.Restore(artifact.Hash, Path.Combine(output, artifact.RelativePath)))
                {
                    throw new IOException("Cannot materialize a generated image.");
                }
            }
            var payload = JsonSerializer.Serialize(new CachedImageResult(info, outputs), ImageCacheJsonContext.Default.CachedImageResult);
            ArtifactCache.WriteAtomic(recordPath, JsonSerializer.SerializeToUtf8Bytes(
                new ImageCacheEnvelope(BuildFingerprint.HashText(payload), payload), ImageCacheJsonContext.Default.ImageCacheEnvelope));
            return info;
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
