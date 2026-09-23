using Kiji.Generation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Kiji.Assets;

/// <summary>Generates responsive WebP image variants.</summary>
/// <param name="options">Image settings, or null to use the defaults.</param>
internal sealed class ImageProcessor(ImageOptions? options = null) : IImageProcessor
{
    /// <summary>
    /// Caps the number of decoded images alive across pages. ImageSharp retains
    /// its own default parallelism; changing it requires workload measurements.
    /// </summary>
    private static readonly SemaphoreSlim ConcurrencyGate = new(Environment.ProcessorCount);

    private readonly ImageOptions _options = options ?? new ImageOptions();
    private readonly SemaphoreSlim _generationGate = ConcurrencyGate;
    private readonly Configuration _configuration = Configuration.Default.Clone();
    internal Func<string, CancellationToken, Task>? BeforeEncodeAsync { get; init; }

    internal ImageProcessor(ImageOptions? options, SemaphoreSlim generationGate, int innerParallelism)
        : this(options)
    {
        _generationGate = generationGate;
        _configuration.MaxDegreeOfParallelism = innerParallelism;
    }

    private static readonly string EncoderIdentity = $"mvid:{typeof(WebpEncoder).Assembly.ManifestModule.ModuleVersionId:N}";

    public string CacheIdentity => BuildFingerprint.HashText(FormattableString.Invariant(
        $"webp-v2:{_options.Quality}:{_options.MaxSourceWidth}:{string.Join(",", _options.Widths)}:{EncoderIdentity}"));

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
        ArgumentNullException.ThrowIfNull(_options.Widths);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxSourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.Quality, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.Quality, 100);
        foreach (var width in _options.Widths)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        }

        var contentHash = BuildFingerprint.HashText(CacheIdentity + ":" + sourceHash);
        using var source = new MemoryStream(bytes, writable: false);
        var identity = await Image.IdentifyAsync(source, cancellationToken);
        source.Position = 0;
        var originalWidth = identity.Width;
        var originalHeight = identity.Height;

        Directory.CreateDirectory(outputDirectory);
        // Another page may still reference an older variant with this basename.
        // Only output reconciliation knows which files are safe to remove.

        var targetWidths = _options.Widths
            .Where(width => width < originalWidth)
            .Append(Math.Min(originalWidth, _options.MaxSourceWidth))
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
            Image? image = null;
            var ownsSlot = false;
            try
            {
                foreach (var targetWidth in targetWidths)
                {
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
                            image ??= await LoadImageWithoutMetadataAsync(source, cancellationToken);
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

    private async Task<Image> LoadImageWithoutMetadataAsync(Stream source, CancellationToken cancellationToken)
    {
        var image = await Image.LoadAsync(new DecoderOptions { Configuration = _configuration }, source, cancellationToken);

        // Remove metadata for privacy/security.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        return image;
    }

    private async Task EncodeVariantAsync(
        Image image,
        int originalWidth,
        int originalHeight,
        int targetWidth,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var encoder = new WebpEncoder
        {
            Quality = _options.Quality,
            FileFormat = WebpFileFormatType.Lossy,
        };

        // Write to a temp file and move into place so concurrent renders of the same
        // image never observe a partially written variant.
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (targetWidth == originalWidth)
            {
                await image.SaveAsync(temporaryPath, encoder, cancellationToken);
            }
            else
            {
                var targetHeight = (int)Math.Round((double)originalHeight * targetWidth / originalWidth);
                using var resized = image.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3,
                }));
                await resized.SaveAsync(temporaryPath, encoder, cancellationToken);
            }

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
