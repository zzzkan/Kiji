using Kiji.Testing;
using Kiji.Assets;
using SkiaSharp;
using Xunit;

namespace Kiji.Imaging.Tests;

public sealed class ImageConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-image-concurrency-{Guid.NewGuid():N}");

    private async Task<string> Source(string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name + ".png");
        using var image = new SKBitmap(32, 32);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, encoded.ToArray());
        return path;
    }

    [Fact]
    public async Task SharedImageGeneratesOnceForConcurrentReferrers()
    {
        var source = await Source("one");
        var count = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = async (_, ct) => { Interlocked.Increment(ref count); entered.TrySetResult(); await release.Task.WaitAsync(ct); },
        };
        var first = ImageArtifactProcessor.ProcessAsync(processor, source, Path.Combine(_root, "shared"), Path.Combine(_root, "cache"), Path.Combine(_root, "work"), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var others = Enumerable.Range(0, 12).Select(_ => ImageArtifactProcessor.ProcessAsync(processor, source,
            Path.Combine(_root, "shared"), Path.Combine(_root, "cache"), Path.Combine(_root, "work"), default)).ToArray();
        release.SetResult();
        var info = await first;
        var results = await Task.WhenAll(others);
        Assert.Equal(1, count);
        Assert.All(results, result => Assert.Equal(info.Variants, result.Variants));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "shared"), "*.webp"));
    }

    [Fact]
    public async Task SharedCustomResults_PublishConcurrentlyWithoutCorruptingImmutableFiles()
    {
        var source = await Source("custom");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writers = 0;
        await using var processor = new TrackingImageProcessor
        {
            BeforeWriteAsync = async ct =>
            {
                if (Interlocked.Increment(ref writers) == 2) { entered.TrySetResult(); }
                await release.Task.WaitAsync(ct);
            },
        };
        var output = Path.Combine(_root, "shared");
        var first = ImageArtifactProcessor.ProcessAsync(processor, source, output, null, Path.Combine(_root, "work"), default);
        var second = ImageArtifactProcessor.ProcessAsync(processor, source, output, null, Path.Combine(_root, "work"), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(Directory.Exists(output));
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(2, processor.Calls);
        var image = Assert.Single(Directory.GetFiles(output, "custom.svg", SearchOption.AllDirectories));
        Assert.Equal(results[0].Variants, results[1].Variants);
        Assert.EndsWith("</svg>", await File.ReadAllTextAsync(image), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedVariantFilename_IsPublishedAndRestoredOnce(bool cached)
    {
        var source = await Source("repeated");
        var processor = new RepeatedVariantImageProcessor();
        var output = Path.Combine(_root, "out");
        var cache = cached ? Path.Combine(_root, "cache", "images") : null;
        var info = await ImageArtifactProcessor.ProcessAsync(processor, source, output, cache, Path.Combine(_root, "work"), default);
        Assert.Equal(2, info.Variants.Count);
        Assert.Equal(info.Variants[0].FileName, info.Variants[1].FileName);
        Assert.DoesNotContain('\\', info.Variants[0].FileName);
        Assert.Single(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
        Directory.Delete(output, recursive: true);
        var restored = await ImageArtifactProcessor.ProcessAsync(processor, source, output, cache, Path.Combine(_root, "work"), default);
        Assert.Equal(info.Variants, restored.Variants);
        Assert.Single(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OldMutableRecipe_CannotBypassSharedOutputFreezing()
    {
        var source = await Source("legacy");
        await using var processor = new TrackingImageProcessor();
        var cacheRoot = Path.Combine(_root, "cache");
        var legacy = Path.Combine(_root, "custom.svg");
        await File.WriteAllTextAsync(legacy, "<svg/>");
        var hash = new Kiji.Generation.ArtifactCache(cacheRoot).Store(legacy);
        var info = new ProcessedImageInfo { OriginalWidth = 2, OriginalHeight = 2, Variants = [new("custom.svg", 2)] };
        var record = new CachedImageResult(info, [new("custom.svg", hash)]);
        var payload = System.Text.Json.JsonSerializer.Serialize(record, ImageCacheJsonContext.Default.CachedImageResult);
        var sourceHash = Kiji.Generation.BuildFingerprint.HashFile(source);
        var oldKey = Kiji.Generation.BuildFingerprint.HashText(processor.CacheIdentity + ":" + Path.GetFileName(source) + ":" + sourceHash);
        Directory.CreateDirectory(Path.Combine(cacheRoot, "image-records"));
        await File.WriteAllTextAsync(Path.Combine(cacheRoot, "image-records", oldKey + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new ImageCacheEnvelope(Kiji.Generation.BuildFingerprint.HashText(payload), payload),
                ImageCacheJsonContext.Default.ImageCacheEnvelope));

        var result = await ImageArtifactProcessor.ProcessAsync(processor, source, Path.Combine(_root, "shared"),
            Path.Combine(cacheRoot, "images"), Path.Combine(_root, "work"), default);
        Assert.Equal(1, processor.Calls);
        Assert.Matches("^[a-f0-9]{16}/custom\\.svg$", Assert.Single(result.Variants).FileName);
    }

    [Fact]
    public async Task DifferentImagesCanEnterGenerationTogether()
    {
        var a = await Source("a");
        var b = await Source("b");
        using var gate = new SemaphoreSlim(2);
        var count = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ImageProcessor(generationGate: gate)
        {
            BeforeEncodeAsync = async (_, ct) =>
            {
                if (Interlocked.Increment(ref count) == 2) { both.TrySetResult(); }
                await both.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            },
        };
        await Task.WhenAll(ImageArtifactProcessor.ProcessAsync(processor, a, Path.Combine(_root, "out"),
                Path.Combine(_root, "cache", "images"), Path.Combine(_root, "work"), default),
            ImageArtifactProcessor.ProcessAsync(processor, b, Path.Combine(_root, "out"),
                Path.Combine(_root, "cache", "images"), Path.Combine(_root, "work"), default));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CanceledWaiterDoesNotReplaceLiveGate_AndGenerationFailureCanRetry()
    {
        var key = Path.Combine(_root, "lock");
        Task<ImageGenerationLease> next;
        using (var owner = await ImageGenerationLock.AcquireAsync(key, default))
        {
            using var cancellation = new CancellationTokenSource();
            var waiter = ImageGenerationLock.AcquireAsync(key, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
            next = ImageGenerationLock.AcquireAsync(key, default).AsTask();
            Assert.False(next.IsCompleted);
        }
        using (await next) { }
        var source = await Source("failure");
        var attempts = 0;
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = (_, _) => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new IOException("test failure")) : Task.CompletedTask,
        };
        await Assert.ThrowsAsync<IOException>(() => processor.ProcessAsync(source, Path.Combine(_root, "out")));
        await processor.ProcessAsync(source, Path.Combine(_root, "out"));
        Assert.Equal(2, attempts);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CanceledProducerReleasesGenerationForAnotherCaller()
    {
        var source = await Source("canceled");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var canceled = new CancellationTokenSource();
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = async (_, ct) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            },
        };
        var first = ImageArtifactProcessor.ProcessAsync(processor, source, Path.Combine(_root, "first"),
            Path.Combine(_root, "cache"), Path.Combine(_root, "work"), canceled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = ImageArtifactProcessor.ProcessAsync(processor, source, Path.Combine(_root, "second"), Path.Combine(_root, "cache"), Path.Combine(_root, "work"), default);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var result = await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, attempts);
        Assert.True(File.Exists(Path.Combine(_root, "second", Assert.Single(result.Variants).FileName)));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }
}
