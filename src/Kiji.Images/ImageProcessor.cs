using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using Kiji.Assets;

namespace Kiji.Images;

/// <summary>
/// Processes images and generates responsive WebP variants.
/// </summary>
public sealed partial class ImageProcessor(ImageOptions? options = null) : IImageAssetProcessor
{
    /// <summary>
    /// Supported image file extensions.
    /// </summary>
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    /// <summary>
    /// Caps total encode/resize concurrency across all pages, since pages themselves
    /// render in parallel and nested parallelism would oversubscribe the CPU.
    /// </summary>
    private static readonly SemaphoreSlim ConcurrencyGate = new(Environment.ProcessorCount);

    private readonly ImageOptions _options = options ?? new ImageOptions();

    /// <summary>
    /// Processes every image found in a source directory and generates responsive variants.
    /// </summary>
    /// <param name="outputDir">The output directory for the generated images.</param>
    /// <param name="postSourceDir">The source directory containing the images.</param>
    /// <returns>A dictionary mapping image reference keys to <see cref="ProcessedImageInfo"/>.</returns>
    public async Task<IReadOnlyDictionary<string, ProcessedImageInfo>> ProcessPostImagesAsync(
        string outputDir,
        string postSourceDir,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(postSourceDir))
        {
            return new Dictionary<string, ProcessedImageInfo>(StringComparer.Ordinal);
        }

        var imageUrls = Directory.EnumerateFiles(postSourceDir, "*.*", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFileName)
            .Where(static fileName => fileName is not null)
            .Cast<string>();

        return await ProcessReferencedImagesAsync(outputDir, postSourceDir, imageUrls, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, ProcessedImageInfo>> ProcessReferencedImagesAsync(
        string outputDirectory,
        string sourceDirectory,
        IEnumerable<string> imageUrls,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(imageUrls);

        var imageInfoLookup = new Dictionary<string, ProcessedImageInfo>(StringComparer.Ordinal);
        var referencedImageKeys = imageUrls
            .Select(ImageReferenceKey.FromMarkdownUrl)
            .Where(static referenceKey => !string.IsNullOrWhiteSpace(referenceKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (referencedImageKeys.Length is 0)
        {
            CleanupUnreferencedGeneratedFiles(outputDirectory, []);
            DeleteDirectoryIfEmpty(outputDirectory);
            return imageInfoLookup;
        }

        var processedImages = new ConcurrentDictionary<string, ProcessedImageInfo>(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            referencedImageKeys,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            },
            async (referenceKey, ct) =>
            {
                var extension = Path.GetExtension(referenceKey).ToLowerInvariant();
                if (!ImageExtensions.Contains(extension))
                {
                    return;
                }

                var sourceFile = Path.Combine(sourceDirectory, referenceKey.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourceFile))
                {
                    return;
                }

                await ConcurrencyGate.WaitAsync(ct);
                try
                {
                    var imageInfo = await ProcessImageAsync(sourceFile, outputDirectory, ct);
                    if (imageInfo is not null)
                    {
                        processedImages[referenceKey] = imageInfo;
                    }
                }
                finally
                {
                    ConcurrencyGate.Release();
                }
            });

        foreach (var (referenceKey, imageInfo) in processedImages)
        {
            imageInfoLookup[referenceKey] = imageInfo;
        }

        CleanupUnreferencedGeneratedFiles(outputDirectory, imageInfoLookup.Values.Select(static info => info.AssetFileNameBase));
        DeleteDirectoryIfEmpty(outputDirectory);

        return imageInfoLookup;
    }

    private async Task<ProcessedImageInfo?> ProcessImageAsync(
        string sourceFile,
        string outputDir,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputDir);

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
        var assetFileNameBase = Path.GetFileName(sourceFile);

        // Calculate content hash of the original image file
        var contentHash = await ComputeFileHashAsync(sourceFile, cancellationToken);

        using var image = await Image.LoadAsync(sourceFile, cancellationToken);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        Console.WriteLine($"Processing image: {Path.GetFileName(sourceFile)} ({originalWidth}x{originalHeight}) [hash: {contentHash}]");

        // Clean up old files with different hashes before generating new ones
        CleanupOldImageFiles(outputDir, assetFileNameBase, contentHash);

        // Remove metadata for privacy/security
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        var availableWidths = new List<int>();

        // Generate responsive sizes (only if smaller than original)
        foreach (var targetWidth in _options.Widths)
        {
            if (targetWidth >= originalWidth)
            {
                continue;
            }

            availableWidths.Add(targetWidth);

            var targetHeight = (int)Math.Round((double)originalHeight * targetWidth / originalWidth);
            var outputFileName = $"{assetFileNameBase}.{contentHash}.{targetWidth}w.webp";
            var outputPath = Path.Combine(outputDir, outputFileName);

            if (File.Exists(outputPath))
            {
                Console.WriteLine($"Skipping: {outputFileName} (already exists)");
                continue;
            }

            await ResizeAndSaveAsync(image, targetWidth, targetHeight, outputPath, cancellationToken);

            Console.WriteLine($"Generated: {Path.GetFullPath(outputPath)} ({targetWidth}x{targetHeight})");
        }

        // Generate original size (capped at MaxSourceWidth)
        var originalOutputWidth = Math.Min(originalWidth, _options.MaxSourceWidth);
        var originalOutputHeight = originalOutputWidth == originalWidth
            ? originalHeight
            : (int)Math.Round((double)originalHeight * originalOutputWidth / originalWidth);
        var originalOutputFileName = $"{assetFileNameBase}.{contentHash}.{originalOutputWidth}w.webp";
        var originalOutputPath = Path.Combine(outputDir, originalOutputFileName);

        if (File.Exists(originalOutputPath))
        {
            Console.WriteLine($"Skipping: {originalOutputFileName} (already exists)");
        }
        else if (originalOutputWidth == originalWidth)
        {
            // No resize needed, just convert to WebP
            await SaveAsWebpAsync(image, originalOutputPath, cancellationToken);
            Console.WriteLine($"Generated: {Path.GetFullPath(originalOutputPath)} ({originalOutputWidth}x{originalOutputHeight}) [original size]");
        }
        else
        {
            await ResizeAndSaveAsync(image, originalOutputWidth, originalOutputHeight, originalOutputPath, cancellationToken);
            Console.WriteLine($"Generated: {Path.GetFullPath(originalOutputPath)} ({originalOutputWidth}x{originalOutputHeight}) [original size]");
        }

        availableWidths.Add(originalOutputWidth);

        return new ProcessedImageInfo
        {
            AssetFileNameBase = assetFileNameBase,
            FileName = fileNameWithoutExt,
            OriginalWidth = originalWidth,
            OriginalHeight = originalHeight,
            AvailableWidths = [.. availableWidths.Order()],
            AspectRatio = (double)originalWidth / originalHeight,
            ContentHash = contentHash
        };
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var hashString = Convert.ToHexString(hashBytes);
        return hashString[..8].ToLowerInvariant();
    }

    private static void CleanupOldImageFiles(string outputDir, string assetFileNameBase, string currentHash)
    {
        if (!Directory.Exists(outputDir))
        {
            return;
        }

        var pattern = $"{assetFileNameBase}.*.webp";
        var files = Directory.GetFiles(outputDir, pattern);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.Contains($".{currentHash}."))
            {
                try
                {
                    File.Delete(file);
                    Console.WriteLine($"Deleted old file: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not delete old file {fileName}: {ex.Message}");
                }
            }
        }
    }

    private async Task ResizeAndSaveAsync(Image source, int targetWidth, int targetHeight, string outputPath, CancellationToken cancellationToken)
    {
        using var resized = source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        resized.Metadata.ExifProfile = null;
        resized.Metadata.IptcProfile = null;
        resized.Metadata.XmpProfile = null;

        await SaveAsWebpAsync(resized, outputPath, cancellationToken);
    }

    private async Task SaveAsWebpAsync(Image image, string outputPath, CancellationToken cancellationToken)
    {
        var encoder = new WebpEncoder
        {
            Quality = _options.Quality,
            FileFormat = WebpFileFormatType.Lossy
        };
        await SaveImageWithRetryAsync(image, outputPath, encoder, cancellationToken);
    }

    private static async Task SaveImageWithRetryAsync(
        Image image,
        string path,
        IImageEncoder encoder,
        CancellationToken cancellationToken,
        int maxRetries = 5,
        int delayMs = 200)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await image.SaveAsync(path, encoder, cancellationToken);
                return;
            }
            catch (IOException ex) when (attempt < maxRetries && ex.Message.Contains("being used by another process"))
            {
                Console.WriteLine($"File locked, retrying in {delayMs}ms ({attempt + 1}/{maxRetries})...");
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }

    private static void CleanupUnreferencedGeneratedFiles(string outputDir, IEnumerable<string> expectedAssetFileNames)
    {
        if (!Directory.Exists(outputDir))
        {
            return;
        }

        var expectedFileNames = expectedAssetFileNames
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(outputDir, "*.webp", SearchOption.TopDirectoryOnly))
        {
            var generatedFileName = Path.GetFileName(file);
            var match = GeneratedVariantRegex().Match(generatedFileName);
            if (!match.Success)
            {
                continue;
            }

            var assetFileNameBase = match.Groups[1].Value;
            if (expectedFileNames.Contains(assetFileNameBase))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                Console.WriteLine($"Deleted unreferenced file: {generatedFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not delete unreferenced file {generatedFileName}: {ex.Message}");
            }
        }
    }

    private static void DeleteDirectoryIfEmpty(string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            return;
        }

        Directory.Delete(outputDir, recursive: false);
    }

    [GeneratedRegex(@"^(.+)\.[0-9a-f]{8}\.\d+w\.webp$")]
    private static partial Regex GeneratedVariantRegex();
}
