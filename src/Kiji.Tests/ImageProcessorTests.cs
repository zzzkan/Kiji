using Kiji.Images;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="ImageProcessor"/>.
/// </summary>
public sealed class ImageProcessorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _sourceDir;
    private readonly string _outputDir;
    private readonly ImageProcessor _processor = new();

    public ImageProcessorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ImageProcessorTests_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testDir, "source");
        _outputDir = Path.Combine(_testDir, "output");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);

        // Fill with a gradient for more realistic content
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(x * 255 / width);
                var g = (byte)(y * 255 / height);
                var b = (byte)((x + y) * 128 / (width + height));
                image[x, y] = new Rgba32(r, g, b);
            }
        }

        await image.SaveAsPngAsync(path);
    }

    #region ProcessPostImagesAsync Tests

    [Fact]
    public async Task ProcessPostImagesAsync_EmptyDirectory_ReturnsEmptyDictionary()
    {
        // Act
        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_NonExistentDirectory_ReturnsEmptyDictionary()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "non-existent");

        // Act
        var result = await _processor.ProcessPostImagesAsync(_outputDir, nonExistentDir);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_SingleImage_GeneratesResponsiveSizes()
    {
        var imagePath = Path.Combine(_sourceDir, "test-image.png");
        await CreateTestImageAsync(imagePath, 1920, 1080);

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("test-image.png"));

        var imageInfo = result["test-image.png"];
        Assert.Equal("test-image.png", imageInfo.AssetFileNameBase);
        Assert.Equal("test-image", imageInfo.FileName);
        Assert.Equal(1920, imageInfo.OriginalWidth);
        Assert.Equal(1080, imageInfo.OriginalHeight);
        Assert.NotEmpty(imageInfo.AvailableWidths);
        Assert.NotEmpty(imageInfo.ContentHash);
        Assert.Equal(8, imageInfo.ContentHash.Length);

        // Verify WebP files were created
        var webpFiles = Directory.GetFiles(_outputDir, "*.webp");
        Assert.NotEmpty(webpFiles);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_SmallImage_LimitedResponsiveSizes()
    {
        var imagePath = Path.Combine(_sourceDir, "small-image.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Single(result);
        var imageInfo = result["small-image.png"];

        // Should only include sizes smaller than original (320) plus the original (500)
        Assert.Contains(320, imageInfo.AvailableWidths);
        Assert.Contains(500, imageInfo.AvailableWidths);
        Assert.DoesNotContain(640, imageInfo.AvailableWidths);
        Assert.DoesNotContain(960, imageInfo.AvailableWidths);
        Assert.DoesNotContain(1280, imageInfo.AvailableWidths);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_VerySmallImage_OnlyOriginalSize()
    {
        var imagePath = Path.Combine(_sourceDir, "tiny-image.png");
        await CreateTestImageAsync(imagePath, 200, 150);

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Single(result);
        var imageInfo = result["tiny-image.png"];

        // Should only include original size since it's smaller than all target widths
        Assert.Single(imageInfo.AvailableWidths);
        Assert.Contains(200, imageInfo.AvailableWidths);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_MultipleImages_ProcessesAll()
    {
        await CreateTestImageAsync(Path.Combine(_sourceDir, "image1.png"), 1000, 600);
        await CreateTestImageAsync(Path.Combine(_sourceDir, "image2.jpg"), 800, 500);

        // Also create a .jpg file (just rename .png for test purposes)
        var jpgPath = Path.Combine(_sourceDir, "image2.jpg");
        File.Move(Path.Combine(_sourceDir, "image2.jpg"), jpgPath + ".tmp", true);
        await CreateTestImageAsync(jpgPath, 800, 500);
        if (File.Exists(jpgPath + ".tmp"))
        {
            File.Delete(jpgPath + ".tmp");
        }

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("image1.png"));
        Assert.True(result.ContainsKey("image2.jpg"));
    }

    [Fact]
    public async Task ProcessPostImagesAsync_NonImageFiles_Ignored()
    {
        await CreateTestImageAsync(Path.Combine(_sourceDir, "valid.png"), 500, 300);
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "readme.txt"), "Not an image");
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "style.css"), "body {}");

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("valid.png"));
    }

    [Fact]
    public async Task ProcessPostImagesAsync_ExistingFiles_SkipsWhenNotForced()
    {
        // Arrange
        var imagePath = Path.Combine(_sourceDir, "existing.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        // First processing
        var result1 = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var webpFiles1 = Directory.GetFiles(_outputDir, "*.webp");
        var firstWriteTimes = webpFiles1.ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

        // Wait a bit
        await Task.Delay(100);

        // Second processing without force
        var result2 = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var webpFiles2 = Directory.GetFiles(_outputDir, "*.webp");

        // Assert
        Assert.Equal(webpFiles1.Length, webpFiles2.Length);
        foreach (var file in webpFiles2)
        {
            if (firstWriteTimes.TryGetValue(file, out var firstWriteTime))
            {
                Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(file));
            }
        }
    }

    [Fact]
    public async Task ProcessPostImagesAsync_AspectRatio_CalculatedCorrectly()
    {
        // Arrange
        var imagePath = Path.Combine(_sourceDir, "aspect.png");
        await CreateTestImageAsync(imagePath, 1600, 900); // 16:9

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        var imageInfo = result["aspect.png"];
        var expectedAspectRatio = 1600.0 / 900.0;
        Assert.Equal(expectedAspectRatio, imageInfo.AspectRatio, precision: 4);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_LargeImage_CappedAtMaxWidth()
    {
        var imagePath = Path.Combine(_sourceDir, "huge.png");
        await CreateTestImageAsync(imagePath, 2500, 1400);

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        var imageInfo = result["huge.png"];

        // Original dimensions should be preserved in info
        Assert.Equal(2500, imageInfo.OriginalWidth);
        Assert.Equal(1400, imageInfo.OriginalHeight);

        // But the largest available width should be capped at 1920
        Assert.Equal(1920, imageInfo.AvailableWidths.Max());
    }

    [Fact]
    public async Task ProcessPostImagesAsync_ContentHash_ChangesWithContent()
    {
        var imagePath = Path.Combine(_sourceDir, "hash-test.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        // First processing
        var result1 = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var hash1 = result1["hash-test.png"].ContentHash;

        // Delete output and create different image with same name
        foreach (var file in Directory.GetFiles(_outputDir))
        {
            File.Delete(file);
        }

        await CreateTestImageAsync(imagePath, 600, 400); // Different dimensions

        // Second processing
        var result2 = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var hash2 = result2["hash-test.png"].ContentHash;

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ProcessPostImagesAsync_SupportedFormats_AllProcessed()
    {
        await CreateTestImageAsync(Path.Combine(_sourceDir, "image.png"), 500, 300);
        await CreateTestImageAsync(Path.Combine(_sourceDir, "image2.jpg"), 500, 300);

        // For JPEG format
        using (var image = new Image<Rgba32>(500, 300))
        {
            var jpegPath = Path.Combine(_sourceDir, "image2.jpg");
            if (File.Exists(jpegPath))
            {
                File.Delete(jpegPath);
            }
            await image.SaveAsJpegAsync(jpegPath);
        }

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ProcessReferencedImagesAsync_OnlyProcessesReferencedImagesAndRemovesStaleVariants()
    {
        await CreateTestImageAsync(Path.Combine(_sourceDir, "used.png"), 1200, 800);
        await CreateTestImageAsync(Path.Combine(_sourceDir, "unused.png"), 800, 600);

        await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        var result = await _processor.ProcessReferencedImagesAsync(
            _outputDir,
            _sourceDir,
            ["used.png"]);

        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp")
            .Select(Path.GetFileName)
            .Where(static fileName => fileName is not null)
            .Cast<string>()
            .ToArray();

        Assert.Single(result);
        Assert.Contains("used.png", result.Keys);
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("used.png.", StringComparison.Ordinal));
        Assert.DoesNotContain(generatedFiles, static fileName => fileName.StartsWith("unused.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessPostImagesAsync_MixedExtensionImages_KeepDistinctGeneratedVariants()
    {
        await CreateTestImageAsync(Path.Combine(_sourceDir, "shared.jpg"), 900, 450);
        await CreateTestImageAsync(Path.Combine(_sourceDir, "shared.png"), 400, 200);

        var result = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);

        var jpgInfo = result["shared.jpg"];
        var pngInfo = result["shared.png"];
        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).ToArray();

        Assert.Equal("shared.jpg", jpgInfo.AssetFileNameBase);
        Assert.Equal("shared.png", pngInfo.AssetFileNameBase);
        Assert.Contains(generatedFiles, static fileName =>
            fileName is not null && fileName.StartsWith("shared.jpg.", StringComparison.Ordinal));
        Assert.Contains(generatedFiles, static fileName =>
            fileName is not null && fileName.StartsWith("shared.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessPostImagesAsync_MixedExtensionCleanup_RemovesOnlyMatchingGeneratedVariants()
    {
        var jpgPath = Path.Combine(_sourceDir, "shared.jpg");
        var pngPath = Path.Combine(_sourceDir, "shared.png");

        await CreateTestImageAsync(jpgPath, 900, 450);
        await CreateTestImageAsync(pngPath, 400, 200);

        var firstResult = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var jpgHash = firstResult["shared.jpg"].ContentHash;
        var oldPngHash = firstResult["shared.png"].ContentHash;

        await CreateTestImageAsync(pngPath, 500, 250);

        var secondResult = await _processor.ProcessPostImagesAsync(_outputDir, _sourceDir);
        var newPngHash = secondResult["shared.png"].ContentHash;
        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).ToArray();
        var expectedJpgFragment = $"shared.jpg.{jpgHash}.";
        var expectedNewPngFragment = $"shared.png.{newPngHash}.";
        var expectedOldPngFragment = $"shared.png.{oldPngHash}.";

        Assert.NotEqual(oldPngHash, newPngHash);
        Assert.Contains(generatedFiles, fileName =>
            fileName is not null && fileName.Contains(expectedJpgFragment, StringComparison.Ordinal));
        Assert.Contains(generatedFiles, fileName =>
            fileName is not null && fileName.Contains(expectedNewPngFragment, StringComparison.Ordinal));
        Assert.DoesNotContain(generatedFiles, fileName =>
            fileName is not null && fileName.Contains(expectedOldPngFragment, StringComparison.Ordinal));
    }

    #endregion
}
