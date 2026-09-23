using Kiji.Assets;
using Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kiji.Tests;

public sealed class ImageProcessorTests : IDisposable
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
    public async Task ProcessImage_SmallImage_LimitedVariants()
    {
        var imagePath = Path.Combine(_sourceDir, "small-image.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        var info = await _processor.ProcessAsync(imagePath, _outputDir);

        Assert.Equal([320, 500], info.Variants.Select(static variant => variant.Width));
    }

    [Fact]
    public async Task ProcessImage_VerySmallImage_OnlyOriginalSize()
    {
        var imagePath = Path.Combine(_sourceDir, "tiny-image.png");
        await CreateTestImageAsync(imagePath, 200, 150);

        var info = await _processor.ProcessAsync(imagePath, _outputDir);

        var variant = Assert.Single(info.Variants);
        Assert.Equal(200, variant.Width);
    }

    [Fact]
    public async Task ProcessImage_LargeImage_CappedAtMaxSourceWidth()
    {
        var imagePath = Path.Combine(_sourceDir, "huge.png");
        await CreateTestImageAsync(imagePath, 2500, 1400);

        var info = await _processor.ProcessAsync(imagePath, _outputDir);

        Assert.Equal(2500, info.OriginalWidth);
        Assert.Equal(1400, info.OriginalHeight);
        Assert.Equal([320, 640, 960, 1280, 1920], info.Variants.Select(static variant => variant.Width));
    }

    [Fact]
    public async Task ProcessImage_ExistingVariants_AreNotRegenerated()
    {
        var encodes = 0;
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = (_, _) => { Interlocked.Increment(ref encodes); return Task.CompletedTask; },
        };
        var imagePath = Path.Combine(_sourceDir, "existing.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        await processor.ProcessAsync(imagePath, _outputDir);
        var initialEncodes = encodes;
        Assert.True(initialEncodes > 0);

        await processor.ProcessAsync(imagePath, _outputDir);

        Assert.Equal(initialEncodes, encodes);
    }

    [Fact]
    public async Task ProcessImage_ContentChange_KeepsVariantsOtherPagesMayReference()
    {
        var imagePath = Path.Combine(_sourceDir, "hash-test.png");
        await CreateTestImageAsync(imagePath, 500, 300);

        var first = await _processor.ProcessAsync(imagePath, _outputDir);
        var firstFileNames = first.Variants.Select(static variant => variant.FileName).ToArray();

        await CreateTestImageAsync(imagePath, 600, 400);

        var second = await _processor.ProcessAsync(imagePath, _outputDir);
        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).ToArray();

        Assert.NotEqual(firstFileNames, [.. second.Variants.Select(static variant => variant.FileName)]);
        foreach (var staleFileName in firstFileNames)
        {
            Assert.Contains(staleFileName, generatedFiles);
        }
    }

    [Fact]
    public async Task ProcessImage_SameBaseNameDifferentExtension_KeepsDistinctVariants()
    {
        var jpgPath = Path.Combine(_sourceDir, "shared.jpg");
        var pngPath = Path.Combine(_sourceDir, "shared.png");
        await CreateTestImageAsync(jpgPath, 900, 450);
        await CreateTestImageAsync(pngPath, 400, 200);

        await _processor.ProcessAsync(jpgPath, _outputDir);
        await _processor.ProcessAsync(pngPath, _outputDir);

        var generatedFiles = Directory.GetFiles(_outputDir, "*.webp").Select(Path.GetFileName).Cast<string>().ToArray();

        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("shared.jpg.", StringComparison.Ordinal));
        Assert.Contains(generatedFiles, static fileName => fileName.StartsWith("shared.png.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessImage_WithCacheDirectory_SurvivesOutputCleanWithoutReencoding()
    {
        var encodes = 0;
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = (_, _) => { Interlocked.Increment(ref encodes); return Task.CompletedTask; },
        };
        var imagePath = Path.Combine(_sourceDir, "cached.png");
        await CreateTestImageAsync(imagePath, 800, 600);

        await ImageArtifactProcessor.ProcessAsync(processor, imagePath, _outputDir, _cacheDir, default);
        var initialEncodes = encodes;
        Assert.True(initialEncodes > 0);

        // Simulate a clean build: output is wiped, cache survives.
        Directory.Delete(_outputDir, recursive: true);

        var info = await ImageArtifactProcessor.ProcessAsync(processor, imagePath, _outputDir, _cacheDir, default);

        foreach (var variant in info.Variants)
        {
            Assert.True(File.Exists(Path.Combine(_outputDir, variant.FileName)));
            var hash = Generation.BuildFingerprint.HashFile(Path.Combine(_outputDir, variant.FileName));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(_cacheDir)!, "images", hash)));
        }

        Assert.Equal(initialEncodes, encodes);
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

        if (Path.GetExtension(path) == ".jpg")
        {
            await image.SaveAsJpegAsync(path);
        }
        else
        {
            await image.SaveAsPngAsync(path);
        }
    }

    [Fact]
    public async Task ProcessImage_ChangedQuality_DoesNotReuseOldEncodedBytes()
    {
        var imagePath = Path.Combine(_sourceDir, "quality.png");
        await CreateTestImageAsync(imagePath, 200, 100);
        var low = new ImageProcessor(new ImageOptions { Quality = 10 });
        var high = new ImageProcessor(new ImageOptions { Quality = 95 });

        var first = Assert.Single((await ImageArtifactProcessor.ProcessAsync(low, imagePath, _outputDir, _cacheDir, default)).Variants);
        var second = Assert.Single((await ImageArtifactProcessor.ProcessAsync(high, imagePath, _outputDir, _cacheDir, default)).Variants);
        Assert.NotEqual(first.FileName, second.FileName);
        Assert.NotEqual(await File.ReadAllBytesAsync(Path.Combine(_outputDir, first.FileName)),
            await File.ReadAllBytesAsync(Path.Combine(_outputDir, second.FileName)));
    }

}
