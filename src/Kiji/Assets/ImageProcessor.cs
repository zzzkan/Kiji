using Kiji.Generation;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Kiji.Assets;

/// <summary>Generates responsive WebP image variants.</summary>
internal sealed class ImageProcessor : IImageProcessor
{
    /// <summary>
    /// Caps the number of decoded images alive across pages.
    /// </summary>
    private static readonly SemaphoreSlim ConcurrencyGate = new(Environment.ProcessorCount);

    private static readonly int[] Widths = [320, 640, 960, 1280];
    private const int MaxSourceWidth = 1920;
    private const int Quality = 80;
    private readonly SemaphoreSlim _generationGate;
    internal Func<string, CancellationToken, Task>? BeforeEncodeAsync { get; init; }

    internal ImageProcessor(SemaphoreSlim? generationGate = null)
    {
        _generationGate = generationGate ?? ConcurrencyGate;
    }

    // Native code is not covered by managed assembly dependency tracking. Include
    // its version and platform so caches cannot cross incompatible native builds.
    private static readonly string EncoderIdentity =
        $"mvid:{typeof(SKWebpEncoder).Assembly.ManifestModule.ModuleVersionId:N}:{SkiaSharpVersion.Native}:{RuntimeInformation.RuntimeIdentifier}";

    public string CacheIdentity => BuildFingerprint.HashText(FormattableString.Invariant(
        $"skia-webp-v1:{Quality}:{MaxSourceWidth}:{string.Join(",", Widths)}:{EncoderIdentity}"));

    /// <inheritdoc/>
    public async Task<ProcessedImageInfo> ProcessAsync(
        string sourceFilePath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        return await ProcessBytesAsync(bytes, Path.GetFileName(sourceFilePath), BuildFingerprint.HashBytes(bytes),
            outputDirectory, cancellationToken);
    }

    internal async Task<ProcessedImageInfo> ProcessBytesAsync(byte[] bytes, string fileNameBase, string sourceHash,
        string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentHash = BuildFingerprint.HashText(CacheIdentity + ":" + sourceHash);
        using var source = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException($"Cannot read image '{fileNameBase}'.");
        var originalWidth = codec.Info.Width;
        var originalHeight = codec.Info.Height;

        Directory.CreateDirectory(outputDirectory);
        // Another page may still reference an older variant with this basename.
        // Only output reconciliation knows which files are safe to remove.

        var targetWidths = Widths
            .Where(width => width < originalWidth)
            .Append(Math.Min(originalWidth, MaxSourceWidth))
            .Distinct()
            .Order()
            .ToArray();

        // Lock the materialized image family, not its source: differing requested
        // width sets must still serialize overlapping variants. Acquire before a
        // global slot so duplicate callers never hold scarce generation capacity.
        var variants = new List<ImageVariant>(targetWidths.Length);
        using (await ImageGenerationLock.AcquireAsync(
            Path.Combine(outputDirectory, $"{fileNameBase}.{contentHash}"), cancellationToken))
        {
            SKBitmap? image = null;
            var ownsSlot = false;
            try
            {
                foreach (var targetWidth in targetWidths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = $"{fileNameBase}.{contentHash}.{targetWidth}w.webp";
                    var materializedPath = Path.Combine(outputDirectory, fileName);

                    if (!File.Exists(materializedPath))
                    {
                        if (!ownsSlot)
                        {
                            await _generationGate.WaitAsync(cancellationToken);
                            ownsSlot = true;
                        }
                        if (!File.Exists(materializedPath))
                        {
                            image ??= DecodeImage(codec, cancellationToken);
                            if (BeforeEncodeAsync is { } beforeEncode)
                            {
                                await beforeEncode(materializedPath, cancellationToken);
                            }
                            await EncodeVariantAsync(image, originalWidth, originalHeight, targetWidth, materializedPath, cancellationToken);
                            BuildOutput.Detail($"Generated: {materializedPath} ({targetWidth}w)");
                        }
                    }

                    variants.Add(new ImageVariant(fileName, targetWidth));
                }

            }
            finally
            {
                image?.Dispose();
                if (ownsSlot) { _generationGate.Release(); }
            }
        }

        return new ProcessedImageInfo
        {
            OriginalWidth = originalWidth,
            OriginalHeight = originalHeight,
            Variants = variants,
        };
    }

    private static SKBitmap DecodeImage(SKCodec codec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Decode the first frame into sRGB pixels. Re-encoding pixels alone drops
        // source EXIF/IPTC/XMP metadata without changing the interpreted colors.
        using var colorSpace = SKColorSpace.CreateSrgb();
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
        var image = new SKBitmap(info);
        try
        {
            var result = codec.GetPixels(info, image.GetPixels());
            if (result != SKCodecResult.Success)
            {
                throw new InvalidDataException($"Cannot decode image: {result}.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static async Task EncodeVariantAsync(
        SKBitmap image,
        int originalWidth,
        int originalHeight,
        int targetWidth,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        // Write to a temp file and move into place so concurrent renders of the same
        // image never observe a partially written variant.
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetHeight = Math.Max(1, (int)Math.Round((double)originalHeight * targetWidth / originalWidth));
            using var resized = targetWidth == originalWidth ? null :
                image.Resize(new SKSizeI(targetWidth, targetHeight), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    ?? throw new InvalidDataException("Cannot resize image.");
            using var pixels = (resized ?? image).PeekPixels();
            using var encoded = SKWebpEncoder.Encode(pixels, new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Quality))
                ?? throw new InvalidDataException("Cannot encode WebP image.");
            cancellationToken.ThrowIfCancellationRequested();
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var stream = encoded.AsStream();
                await stream.CopyToAsync(output, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

}
