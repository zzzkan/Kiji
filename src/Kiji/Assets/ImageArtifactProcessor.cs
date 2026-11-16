using System.Text.Json;
using Kiji.Generation;
using Kiji.Rendering;

namespace Kiji.Assets;

/// <summary>Kiji owns persistence; encoders only write to isolated working directories.</summary>
internal static class ImageArtifactProcessor
{
    internal static async Task<ProcessedImageInfo> ProcessAsync(IImageProcessor processor,
        string source, string output, string? cacheDirectory, string workDirectory, CancellationToken cancellationToken)
    {
        var sourceBytes = await File.ReadAllBytesAsync(source, cancellationToken);
        var sourceHash = BuildFingerprint.HashBytes(sourceBytes);
        var sourceName = processor is ImageProcessor ? NormalizeCasing(Path.GetFileName(source)) : Path.GetFileName(source);
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
                return await builtIn.ProcessBytesAsync(sourceBytes, sourceName, sourceHash, output, cancellationToken);
            }
            return await GenerateAsync(processor, sourceBytes, sourceHash, output, null, null, sourceName, workDirectory, cancellationToken);
        }
        var root = Path.GetDirectoryName(cacheDirectory)!;
        var cache = new ArtifactCache(root);
        // Older recipes may contain mutable or platform-specific variant paths.
        var key = BuildFingerprint.HashText("shared-images-v1:" + identity + ":" + sourceName + ":" + sourceHash);
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

        return await GenerateAsync(processor, sourceBytes, sourceHash, output, cache, recordPath, sourceName, workDirectory, cancellationToken);
    }

    private static async Task<ProcessedImageInfo> GenerateAsync(IImageProcessor processor,
        byte[] sourceBytes, string sourceHash, string output, ArtifactCache? cache, string? recordPath, string sourceName, string workDirectory, CancellationToken cancellationToken)
    {
        // Work never belongs to public output, including canceled/terminated builds.
        var work = Path.Combine(workDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var generated = Path.Combine(work, "output");
            Directory.CreateDirectory(generated);
            ProcessedImageInfo info;
            if (processor is ImageProcessor builtIn)
            {
                info = await builtIn.ProcessBytesAsync(sourceBytes, sourceName, sourceHash, generated, cancellationToken);
            }
            else
            {
                var sourceCopy = Path.Combine(work, "source", sourceName);
                Directory.CreateDirectory(Path.GetDirectoryName(sourceCopy)!);
                await File.WriteAllBytesAsync(sourceCopy, sourceBytes, cancellationToken);
                info = await processor.ProcessAsync(sourceCopy, generated, cancellationToken);
                // Custom processors may change bytes without changing their filenames.
                // Immutable destinations preserve the versions used by carried pages.
                var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
                info = info with
                {
                    Variants = [.. info.Variants.Select(variant =>
                {
                    // Several widths may intentionally use the same generated file.
                    if (!frozen.TryGetValue(variant.FileName, out var name))
                    {
                        name = FreezeVariant(generated, variant.FileName);
                        frozen.Add(variant.FileName, name);
                    }
                    return variant with { FileName = name };
                })]
                };
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
                if (processor is not ImageProcessor) { PageRenderContext.Current?.SharedRenders?.AddOutput(destination); }
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

    // Fold through uppercase before choosing lowercase URI spelling, so simple
    // case aliases (including the Greek sigma forms) publish the same filename.
    private static string NormalizeCasing(string path) => NormalizeFileName(path).ToUpperInvariant().ToLowerInvariant().Replace(":", "%3A", StringComparison.Ordinal);

    private static string NormalizeFileName(string name)
    {
        if (name.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0) { return name.Normalize(); }
        // Invariant globalization may leave invalid UTF-16 unchanged instead of
        // throwing from Normalize. Escape it before creating filenames and URLs.
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsHighSurrogate(character) && index + 1 < name.Length && char.IsLowSurrogate(name[index + 1]))
            { result.Append(character).Append(name[++index]); }
            else if (char.IsSurrogate(character)) { result.Append('%').Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)); }
            else { result.Append(character); }
        }
        return result.ToString().Normalize();
    }

    private static string FreezeVariant(string directory, string fileName)
    {
        if (!BuildManifest.IsRelativeOutput(fileName)) { throw new InvalidDataException("Invalid image variant path."); }
        var source = Path.Combine(directory, fileName);
        var hash = BuildFingerprint.HashFile(source);
        if (!ArtifactCache.IsHash(hash)) { throw new IOException($"Cannot read generated image variant '{fileName}'."); }
        var name = hash[..16] + "/" + NormalizeCasing(fileName.Replace('\\', '/'));
        var destination = Path.Combine(directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
        return name;
    }
}
