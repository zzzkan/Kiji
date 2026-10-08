using Kiji.Assets;
using Xunit;
using SkiaSharp;
using System.Buffers.Binary;

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

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, ".jpg")]
    [InlineData(SKEncodedImageFormat.Png, ".png")]
    [InlineData(SKEncodedImageFormat.Webp, ".webp")]
    public async Task ProcessImage_FormatsProduceDecodableWebpWithExpectedDimensions(SKEncodedImageFormat format, string extension)
    {
        var path = Path.Combine(_sourceDir, "format" + extension);
        using var source = new SKBitmap(800, 400);
        source.Erase(SKColors.Red);
        using var encoded = source.Encode(format, 100);
        await File.WriteAllBytesAsync(path, encoded.ToArray());

        var result = await _processor.ProcessAsync(path, _outputDir);

        Assert.Equal(800, result.OriginalWidth);
        Assert.Equal(400, result.OriginalHeight);
        Assert.Equal([320, 640, 800], result.Variants.Select(static variant => variant.Width));
        foreach (var variant in result.Variants)
        {
            using var codec = SKCodec.Create(Path.Combine(_outputDir, variant.FileName));
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.Equal(variant.Width, codec.Info.Width);
            Assert.Equal(variant.Width / 2, codec.Info.Height);
            using var bitmap = SKBitmap.Decode(codec);
            var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            Assert.InRange(pixel.Red, (byte)240, byte.MaxValue);
            Assert.InRange(pixel.Green, byte.MinValue, (byte)15);
            Assert.InRange(pixel.Blue, byte.MinValue, (byte)15);
        }
    }

    [Fact]
    public async Task ProcessImage_ResizingPreservesTransparency()
    {
        var path = Path.Combine(_sourceDir, "transparent.png");
        using var source = new SKBitmap(800, 400);
        source.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(400, 0, 400, 400, paint);
        }
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, encoded.ToArray());

        var result = await _processor.ProcessAsync(path, _outputDir);

        foreach (var variant in result.Variants)
        {
            using var bitmap = SKBitmap.Decode(Path.Combine(_outputDir, variant.FileName));
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width / 4, bitmap.Height / 2).Alpha);
            Assert.Equal(255, bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height / 2).Alpha);
        }
    }

    [Fact]
    public async Task ProcessImage_AnimatedGifUsesFirstFrame()
    {
        // Two one-pixel frames, red then blue, with a global two-color palette.
        var path = Path.Combine(_sourceDir, "animated.gif");
        await File.WriteAllBytesAsync(path, Convert.FromHexString(
            "47494638396101000100800000FF00000000FF" +
            "21F904000A0000002C0000000001000100000202440100" +
            "21F904000A0000002C00000000010001000002024C01003B"));
        using (var original = SKCodec.Create(path)) { Assert.Equal(2, original.FrameCount); }

        var result = await _processor.ProcessAsync(path, _outputDir);

        using var codec = SKCodec.Create(Path.Combine(_outputDir, Assert.Single(result.Variants).FileName));
        Assert.InRange(codec.FrameCount, 0, 1);
        using var bitmap = SKBitmap.Decode(codec);
        Assert.InRange(bitmap.GetPixel(0, 0).Red, (byte)240, byte.MaxValue);
        Assert.InRange(bitmap.GetPixel(0, 0).Blue, byte.MinValue, (byte)15);
    }

    [Fact]
    public async Task ProcessImage_DropsSourceExifMetadata()
    {
        using var source = new SKBitmap(16, 8);
        source.Erase(SKColors.Blue);
        using var encoded = source.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = encoded.ToArray();
        // Valid little-endian EXIF with one ImageDescription ASCII entry.
        var exif = Convert.FromHexString("45786966000049492A000800000001000E010200080000001A00000000000000")
            .Concat("private\0"u8.ToArray()).ToArray();
        var withMetadata = new byte[jpeg.Length + exif.Length + 4];
        jpeg.AsSpan(0, 2).CopyTo(withMetadata);
        withMetadata[2] = 0xff;
        withMetadata[3] = 0xe1;
        BinaryPrimitives.WriteUInt16BigEndian(withMetadata.AsSpan(4), checked((ushort)(exif.Length + 2)));
        exif.CopyTo(withMetadata, 6);
        jpeg.AsSpan(2).CopyTo(withMetadata.AsSpan(6 + exif.Length));
        var path = Path.Combine(_sourceDir, "metadata.jpg");
        await File.WriteAllBytesAsync(path, withMetadata);

        var result = await _processor.ProcessAsync(path, _outputDir);

        var webp = await File.ReadAllBytesAsync(Path.Combine(_outputDir, Assert.Single(result.Variants).FileName));
        using var bitmap = SKBitmap.Decode(webp);
        Assert.Equal(16, bitmap.Width);
        for (var offset = 12; offset < webp.Length;)
        {
            var chunk = System.Text.Encoding.ASCII.GetString(webp, offset, 4);
            Assert.NotEqual("EXIF", chunk);
            Assert.NotEqual("XMP ", chunk);
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(offset + 4)));
            offset += 8 + length + (length & 1);
        }
    }

    [Fact]
    public async Task ProcessImage_TruncatedInputDoesNotPublishVariants()
    {
        var path = Path.Combine(_sourceDir, "truncated.png");
        await CreateTestImageAsync(path, 800, 400);
        var bytes = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, bytes[..(bytes.Length / 2)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.ProcessAsync(path, _outputDir));

        Assert.Empty(Directory.GetFiles(_outputDir));
    }

    private static async Task CreateTestImageAsync(string path, int width, int height)
    {
        using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        // Fill with a gradient for more realistic content
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(x * 255 / width);
                var g = (byte)(y * 255 / height);
                var b = (byte)((x + y) * 128 / (width + height));
                image.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        if (Path.GetExtension(path) == ".jpg")
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            await File.WriteAllBytesAsync(path, encoded.ToArray());
        }
        else
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(path, encoded.ToArray());
        }
    }

}
