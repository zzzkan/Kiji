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
        var sourceBytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sourceHash = BuildFingerprint.HashBytes(sourceBytes);
        var dependencies = PageRenderContext.Current?.Dependencies;
        dependencies?.AddFile(source, sourceHash);
        var identity = cacheDirectory is null && dependencies is null ? null : ImageProcessorIdentity.Get(processor);
        dependencies?.AddValue("image-processor", identity);
        dependencies?.AddImageRequest(source, output);
        if (cacheDirectory is null || identity is null)
        {
            dependencies?.DisableCache();
            if (processor is ImageProcessor builtIn)
            {
                // Development can reuse the deterministic built-in variants already on disk.
                return await builtIn.ProcessBytesAsync(sourceBytes, Path.GetFileName(source), sourceHash, output, cancellationToken);
            }
            return await GenerateAsync(processor, source, sourceBytes, sourceHash, output, null, null, cancellationToken);
        }
        var root = Path.GetDirectoryName(cacheDirectory)!;
        var cache = new ArtifactCache(root);
        var key = BuildFingerprint.HashText(identity + ":" + Path.GetFileName(source) + ":" + sourceHash);
        dependencies?.AddImage(key);
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

        return await GenerateAsync(processor, source, sourceBytes, sourceHash, output, cache, recordPath, cancellationToken);
    }

    private static async Task<ProcessedImageInfo> GenerateAsync(IImageProcessor processor, string source,
        byte[] sourceBytes, string sourceHash, string output, ArtifactCache? cache, string? recordPath, CancellationToken cancellationToken)
    {
        var work = recordPath is null
            ? Path.Combine(output, ".work-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(recordPath))!, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var generated = Path.Combine(work, "output");
            Directory.CreateDirectory(generated);
            ProcessedImageInfo info;
            if (processor is ImageProcessor builtIn)
            {
                info = await builtIn.ProcessBytesAsync(sourceBytes, Path.GetFileName(source), sourceHash, generated, cancellationToken);
            }
            else
            {
                var sourceCopy = Path.Combine(work, "source", Path.GetFileName(source));
                Directory.CreateDirectory(Path.GetDirectoryName(sourceCopy)!);
                await File.WriteAllBytesAsync(sourceCopy, sourceBytes, cancellationToken);
                info = await processor.ProcessAsync(sourceCopy, generated, cancellationToken);
                // Custom processors may change bytes without changing their filenames.
                // Immutable destinations preserve the versions used by carried pages.
                info = info with { Variants = [.. info.Variants.Select(variant => FreezeVariant(generated, variant))] };
            }
            cancellationToken.ThrowIfCancellationRequested();
            var outputs = info.Variants.Select(variant =>
            {
                if (!BuildManifest.IsRelativeOutput(variant.FileName)) { throw new InvalidDataException("Invalid image variant path."); }
                var path = Path.Combine(generated, variant.FileName);
                return new BuildManifestOutput(variant.FileName, cache?.Store(path) ?? BuildFingerprint.HashFile(path));
            }).ToArray();
            foreach (var artifact in outputs)
            {
                var destination = Path.Combine(output, artifact.RelativePath);
                if (cache is null)
                {
                    var existing = BuildFingerprint.HashFile(destination);
                    if (existing == artifact.Hash) { continue; }
                    try
                    {
                        ArtifactCache.WriteAtomic(destination, await File.ReadAllBytesAsync(
                            Path.Combine(generated, artifact.RelativePath), cancellationToken), overwrite: existing != BuildFingerprint.Missing);
                    }
                    catch (IOException) when (BuildFingerprint.HashFile(destination) == artifact.Hash)
                    {
                        // Another writer published the same immutable bytes first.
                    }
                }
                else if (!cache.Restore(artifact.Hash, destination))
                {
                    throw new IOException("Cannot materialize a generated image.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (recordPath is not null)
            {
                var payload = JsonSerializer.Serialize(new CachedImageResult(info, outputs), ImageCacheJsonContext.Default.CachedImageResult);
                ArtifactCache.WriteAtomic(recordPath, JsonSerializer.SerializeToUtf8Bytes(
                    new ImageCacheEnvelope(BuildFingerprint.HashText(payload), payload), ImageCacheJsonContext.Default.ImageCacheEnvelope));
            }
            return info;
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    private static ImageVariant FreezeVariant(string directory, ImageVariant variant)
    {
        if (!BuildManifest.IsRelativeOutput(variant.FileName)) { throw new InvalidDataException("Invalid image variant path."); }
        var source = Path.Combine(directory, variant.FileName);
        var hash = BuildFingerprint.HashFile(source);
        if (!ArtifactCache.IsHash(hash)) { throw new IOException($"Cannot read generated image variant '{variant.FileName}'."); }
        var name = Path.Combine(hash[..16], variant.FileName);
        var destination = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
        return variant with { FileName = name };
    }
}
