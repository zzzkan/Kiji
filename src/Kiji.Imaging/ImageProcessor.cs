using Kiji.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace Kiji.Assets;

internal sealed class ImageProcessor : IContentAddressedImageProcessor
{
    private static readonly SemaphoreSlim ConcurrencyGate = new(Environment.ProcessorCount);

    private static readonly int[] DefaultWidths = [320, 640, 960, 1280];

    private const int MaxWebpDimension = 16383;

    private static string EncoderIdentity => field ??=
        $"mvid:{typeof(SKWebpEncoder).Assembly.ManifestModule.ModuleVersionId:N}:{SkiaSharpVersion.Native}:{RuntimeInformation.RuntimeIdentifier}";

    private readonly int[] _widths;
    private readonly int _maxWidth;
    private readonly int _quality;
    private readonly SemaphoreSlim _generationGate;
    private readonly ILogger _logger;
    internal Func<string, CancellationToken, Task>? BeforeEncodeAsync { get; init; }

    internal ImageProcessor(ImageProcessorOptions? options = null, SemaphoreSlim? generationGate = null, ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        options ??= new ImageProcessorOptions();
        _maxWidth = options.MaxWidth;
        _quality = options.Quality;
        _widths = [.. (options.Widths ?? DefaultWidths)
            .Where(width => width < _maxWidth).Distinct().Order()];
        _generationGate = generationGate ?? ConcurrencyGate;
    }

    public string CacheIdentity => field ??= BuildFingerprint.HashText(FormattableString.Invariant(
        $"skia-webp-v2:{_quality}:{_maxWidth}:{string.Join(",", _widths)}:{EncoderIdentity}"));

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

    public async Task<ProcessedImageInfo> ProcessBytesAsync(byte[] bytes, string fileNameBase, string sourceHash,
        string outputDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentHash = BuildFingerprint.HashText(CacheIdentity + ":" + sourceHash)[..16];
        using var source = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException($"Cannot read image '{fileNameBase}'.");
        var origin = codec.EncodedOrigin;
        var swapsAxes = SwapsAxes(origin);
        var originalWidth = swapsAxes ? codec.Info.Height : codec.Info.Width;
        var originalHeight = swapsAxes ? codec.Info.Width : codec.Info.Height;

        Directory.CreateDirectory(outputDirectory);

        var largestWidth = Math.Min(Math.Min(originalWidth, _maxWidth), MaxWebpDimension);
        if ((long)originalHeight * largestWidth > (long)MaxWebpDimension * originalWidth)
        {
            largestWidth = (int)((long)MaxWebpDimension * originalWidth / originalHeight);
        }
        if (largestWidth < 1)
        {
            throw new InvalidDataException(
                $"Image '{fileNameBase}' ({originalWidth}x{originalHeight}) is too narrow for its height to store as WebP, which holds at most {MaxWebpDimension} pixels per side.");
        }

        var targetWidths = _widths
            .Where(width => width < largestWidth)
            .Append(largestWidth)
            .ToArray();

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
                            await EncodeVariantAsync(image, originalWidth, originalHeight, targetWidth, _quality, materializedPath, cancellationToken);
                            _logger.ImageVariantGenerated(materializedPath, targetWidth);
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
            return ApplyOrigin(image, codec.EncodedOrigin);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static SKBitmap ApplyOrigin(SKBitmap image, SKEncodedOrigin origin)
    {
        float w = image.Width;
        float h = image.Height;
        SKMatrix? transform = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => null,
        };
        if (transform is not { } matrix)
        {
            return image;
        }

        var size = SwapsAxes(origin) ? new SKSizeI(image.Height, image.Width) : new SKSizeI(image.Width, image.Height);
        var oriented = new SKBitmap(image.Info.WithSize(size));
        try
        {
            using (var canvas = new SKCanvas(oriented))
            using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
            {
                canvas.SetMatrix(matrix);
                canvas.DrawBitmap(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            }
            image.Dispose();
            return oriented;
        }
        catch
        {
            oriented.Dispose();
            throw;
        }
    }

    private static async Task EncodeVariantAsync(
        SKBitmap image,
        int originalWidth,
        int originalHeight,
        int targetWidth,
        int quality,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, $"{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetHeight = Math.Max(1, (int)Math.Round((double)originalHeight * targetWidth / originalWidth));
            using var resized = targetWidth == originalWidth ? null :
                image.Resize(new SKSizeI(targetWidth, targetHeight), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    ?? throw new InvalidDataException("Cannot resize image.");
            using var pixels = (resized ?? image).PeekPixels();
            using var encoded = SKWebpEncoder.Encode(pixels, new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality))
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
