using System.Text.RegularExpressions;
using Kiji.Images;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="ImageProcessor"/>.
/// </summary>
public sealed partial class ImageProcessorTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _sourceDir;
    private readonly string _outputDir;
    private readonly string _cacheDir;
    private readonly ImageProcessor _processor = new();

    public ImageProcessorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ImageProcessorTests_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testDir, "source");
        _outputDir = Path.Combine(_testDir, "output");
        _cacheDir = Path.Combine(_testDir, "cache");
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

    [Fact]
    public async Task ProcessImage_LargeImage_GeneratesAscendingVariants()
    {
        var imagePath = Path.Combine(_sourceDir, "test-image.png");
        await CreateTestImageAsync(imagePath, 1920, 1080);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir);

        Assert.Equal(1920, info.OriginalWidth);
        Assert.Equal(1080, info.OriginalHeight);
        Assert.Equal([320, 640, 960, 1280, 1920], info.Variants.Select(static variant => variant.Width));

        foreach (var variant in info.Variants)
        {
            Assert.Matches(VariantFileNameRegex(), variant.FileName);
            Assert.True(File.Exists(Path.Combine(_outputDir, variant.FileName)));
        }
    }

    [Fact]
    public async Task ProcessImage_SmallImage_LimitedVariants()
    {
        var imagePath = Path.Combine(_sourceDir, "small-image.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir);

        Assert.Equal([320, 500], info.Variants.Select(static variant => variant.Width));
    }

    [Fact]
    public async Task ProcessImage_VerySmallImage_OnlyOriginalSize()
    {
        var imagePath = Path.Combine(_sourceDir, "tiny-image.png");
        await CreateTestImageAsync(imagePath, 200, 150);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir);

        var variant = Assert.Single(info.Variants);
        Assert.Equal(200, variant.Width);
    }

    [Fact]
    public async Task ProcessImage_LargeImage_CappedAtMaxSourceWidth()
    {
        var imagePath = Path.Combine(_sourceDir, "huge.png");
        await CreateTestImageAsync(imagePath, 2500, 1400);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir);

        Assert.Equal(2500, info.OriginalWidth);
        Assert.Equal(1400, info.OriginalHeight);
        Assert.Equal(1920, info.Variants[^1].Width);
    }

    [Fact]
    public async Task ProcessImage_ExistingVariants_AreNotRegenerated()
    {
        var imagePath = Path.Combine(_sourceDir, "existing.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        await _processor.ProcessImageAsync(imagePath, _outputDir);
        var firstWriteTimes = Directory.GetFiles(_outputDir, "*.webp")
            .ToDictionary(static file => file, static file => File.GetLastWriteTimeUtc(file));

        await Task.Delay(100);

        await _processor.ProcessImageAsync(imagePath, _outputDir);

        foreach (var (file, firstWriteTime) in firstWriteTimes)
        {
            Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public async Task ProcessImage_ContentChange_ReplacesStaleVariants()
    {
        var imagePath = Path.Combine(_sourceDir, "hash-test.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        var first = await _processor.ProcessImageAsync(imagePath, _outputDir);
        var firstFileNames = first.Variants.Select(static variant => variant.FileName).ToArray();

        await CreateTestImageAsync(imagePath, 600, 400);

        var second = await _processor.ProcessImageAsync(imagePath, _outputDir);
        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).ToArray();

        Assert.NotEqual(firstFileNames, [.. second.Variants.Select(static variant => variant.FileName)]);
        foreach (var staleFileName in firstFileNames)
        {
            Assert.DoesNotContain(staleFileName, generatedFiles);
        }
    }

    [Fact]
    public async Task ProcessImage_SameBaseNameDifferentExtension_KeepsDistinctVariants()
    {
        var jpgPath = Path.Combine(_sourceDir, "shared.jpg");
        var pngPath = Path.Combine(_sourceDir, "shared.png");
        await CreateTestImageAsync(jpgPath, 900, 450);
        await CreateTestImageAsync(pngPath, 400, 200);

        await _processor.ProcessImageAsync(jpgPath, _outputDir);
        await _processor.ProcessImageAsync(pngPath, _outputDir);

        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).Cast<string>().ToArray();

        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("shared.jpg.", StringComparison.Ordinal));
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("shared.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessImage_WithCacheDirectory_MaterializesInCacheAndCopiesToOutput()
    {
        var imagePath = Path.Combine(_sourceDir, "cached.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir, _cacheDir);

        foreach (var variant in info.Variants)
        {
            Assert.True(File.Exists(Path.Combine(_cacheDir, variant.FileName)));
            Assert.True(File.Exists(Path.Combine(_outputDir, variant.FileName)));
        }
    }

    [Fact]
    public async Task ProcessImage_WithCacheDirectory_SurvivesOutputCleanWithoutReencoding()
    {
        var imagePath = Path.Combine(_sourceDir, "cached.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        await _processor.ProcessImageAsync(imagePath, _outputDir, _cacheDir);
        var cacheWriteTimes = Directory.GetFiles(_cacheDir, "*.webp")
            .ToDictionary(static file => file, static file => File.GetLastWriteTimeUtc(file));

        // Simulate a clean build: output is wiped, cache survives.
        Directory.Delete(_outputDir, recursive: true);
        await Task.Delay(100);

        var info = await _processor.ProcessImageAsync(imagePath, _outputDir, _cacheDir);

        foreach (var variant in info.Variants)
        {
            Assert.True(File.Exists(Path.Combine(_outputDir, variant.FileName)));
        }

        foreach (var (file, writeTime) in cacheWriteTimes)
        {
            Assert.Equal(writeTime, File.GetLastWriteTimeUtc(file));
        }
    }

    [Fact]
    public async Task ProcessImage_SupportedFormats_AllProcessed()
    {
        var pngPath = Path.Combine(_sourceDir, "image.png");
        await CreateTestImageAsync(pngPath, 500, 300);

        var jpegPath = Path.Combine(_sourceDir, "image2.jpg");
        using (var image = new Image<Rgba32>(500, 300))
        {
            await image.SaveAsJpegAsync(jpegPath);
        }

        var pngInfo = await _processor.ProcessImageAsync(pngPath, _outputDir);
        var jpegInfo = await _processor.ProcessImageAsync(jpegPath, _outputDir);

        Assert.NotEmpty(pngInfo.Variants);
        Assert.NotEmpty(jpegInfo.Variants);
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

    [GeneratedRegex(@"^.+\.[0-9a-f]{8}\.\d+w\.webp$")]
    private static partial Regex VariantFileNameRegex();
}
