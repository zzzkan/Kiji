using System.IO.Hashing;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

using Kiji.Assets;
namespace Kiji.Benchmarks;

/// <summary>Generates responsive WebP image variants.</summary>
/// <param name="options">Image settings, or null to use the defaults.</param>
internal sealed class BaselineImageProcessor(ImageOptions? options = null, bool guardPublication = false) : IImageAssetProcessor
{
    // Allocation-only comparison: protect publication/copy from the original's
    // Windows sharing violation, without deduplicating decode/resize/encode work.
    private readonly Lock _publicationGate = new();
    /// <summary>
    /// Caps total encode/resize concurrency across all pages, since pages themselves
    /// render in parallel and nested parallelism would oversubscribe the CPU.
    /// </summary>
    private static readonly SemaphoreSlim ConcurrencyGate = new(Environment.ProcessorCount);

    private readonly ImageOptions _options = options ?? new ImageOptions();

    /// <inheritdoc/>
    public async Task<ProcessedImageInfo> ProcessImageAsync(
        string sourceFilePath,
        string outputDirectory,
        string? cacheDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(_options.Widths);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxSourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.Quality, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.Quality, 100);
        foreach (var width in _options.Widths)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        }

        var contentHash = await ComputeFileHashAsync(sourceFilePath, cancellationToken);
        var identity = await Image.IdentifyAsync(sourceFilePath, cancellationToken);
        var originalWidth = identity.Width;
        var originalHeight = identity.Height;

        var fileNameBase = Path.GetFileName(sourceFilePath);
        var materializeDirectory = cacheDirectory ?? outputDirectory;
        Directory.CreateDirectory(materializeDirectory);
        // Another page may still reference an older variant with this basename.
        // Only output reconciliation knows which files are safe to remove.

        var targetWidths = _options.Widths
            .Where(width => width < originalWidth)
            .Append(Math.Min(originalWidth, _options.MaxSourceWidth))
            .Distinct()
            .Order()
            .ToArray();

        Image? image = null;
        try
        {
            var variants = new List<ImageVariant>(targetWidths.Length);
            foreach (var targetWidth in targetWidths)
            {
                var fileName = $"{fileNameBase}.{contentHash}.{targetWidth}w.webp";
                var materializedPath = Path.Combine(materializeDirectory, fileName);

                if (!File.Exists(materializedPath))
                {
                    await ConcurrencyGate.WaitAsync(cancellationToken);
                    try
                    {
                        if (!File.Exists(materializedPath))
                        {
                            image ??= await LoadImageWithoutMetadataAsync(sourceFilePath, cancellationToken);
                            await EncodeVariantAsync(image, originalWidth, originalHeight, targetWidth, materializedPath, cancellationToken);
                            BuildOutput.Detail($"Generated: {materializedPath} ({targetWidth}w)");
                        }
                    }
                    finally
                    {
                        ConcurrencyGate.Release();
                    }
                }

                if (cacheDirectory is not null)
                {
                    CopyIntoOutput(materializedPath, outputDirectory, fileName);
                }

                variants.Add(new ImageVariant(fileName, targetWidth));
            }

            return new ProcessedImageInfo
            {
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                Variants = variants,
            };
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static async Task<Image> LoadImageWithoutMetadataAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        var image = await Image.LoadAsync(sourceFilePath, cancellationToken);

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

            if (guardPublication)
            {
                lock (_publicationGate) { File.Move(temporaryPath, destinationPath, overwrite: true); }
            }
            else { File.Move(temporaryPath, destinationPath, overwrite: true); }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void CopyIntoOutput(string materializedPath, string outputDirectory, string fileName)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, fileName);
        if (File.Exists(outputPath))
        {
            return;
        }

        try
        {
            if (guardPublication)
            {
                lock (_publicationGate) { File.Copy(materializedPath, outputPath, overwrite: false); }
            }
            else { File.Copy(materializedPath, outputPath, overwrite: false); }
        }
        catch (IOException) when (File.Exists(outputPath))
        {
            // A concurrent render copied the same variant first.
        }
    }

    private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hasher = new XxHash128();
        // Encoding settings and encoder upgrades must not reuse earlier bytes.
        hasher.Append(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"webp-v1:{_options.Quality}:{typeof(WebpEncoder).Assembly.ManifestModule.ModuleVersionId:N}:")));
        await hasher.AppendAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hasher.GetCurrentHash());
    }
}
