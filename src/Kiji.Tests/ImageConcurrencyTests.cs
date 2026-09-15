using Kiji.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Kiji.Tests;

public sealed class ImageConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-image-concurrency-{Guid.NewGuid():N}");

    private async Task<string> Source(string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name + ".png");
        using var image = new Image<Rgba32>(32, 32);
        await image.SaveAsPngAsync(path);
        return path;
    }

    [Fact]
    public async Task SharedImageGeneratesOnceAndCopiesToEveryPage()
    {
        var source = await Source("one");
        var count = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = async (_, ct) => { Interlocked.Increment(ref count); entered.TrySetResult(); await release.Task.WaitAsync(ct); },
        };
        var first = processor.ProcessImageAsync(source, Path.Combine(_root, "a"), Path.Combine(_root, "cache"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var others = Enumerable.Range(0, 12).Select(i => processor.ProcessImageAsync(source,
            Path.Combine(_root, "page" + i), Path.Combine(_root, "cache"))).ToArray();
        release.SetResult();
        var info = await first;
        await Task.WhenAll(others);
        Assert.Equal(1, count);
        for (var i = 0; i < others.Length; i++)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(_root, "a", info.Variants[0].FileName)),
                File.ReadAllBytes(Path.Combine(_root, "page" + i, info.Variants[0].FileName)));
        }
    }

    [Fact]
    public async Task DifferentImagesCanEnterGenerationTogether()
    {
        var a = await Source("a");
        var b = await Source("b");
        using var gate = new SemaphoreSlim(2);
        var count = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new ImageProcessor(null, gate, 1)
        {
            BeforeEncodeAsync = async (_, ct) =>
            {
                if (Interlocked.Increment(ref count) == 2) { both.TrySetResult(); }
                await both.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            },
        };
        await Task.WhenAll(processor.ProcessImageAsync(a, Path.Combine(_root, "out")),
            processor.ProcessImageAsync(b, Path.Combine(_root, "out")));
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
            // The gate remains live until the owner and waiting acquisition leave.
        }
        using (await next) { }
        var source = await Source("failure");
        var attempts = 0;
        var processor = new ImageProcessor
        {
            BeforeEncodeAsync = (_, _) => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new IOException("test failure")) : Task.CompletedTask,
        };
        await Assert.ThrowsAsync<IOException>(() => processor.ProcessImageAsync(source, Path.Combine(_root, "out")));
        await processor.ProcessImageAsync(source, Path.Combine(_root, "out"));
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
        var first = processor.ProcessImageAsync(source, Path.Combine(_root, "first"),
            Path.Combine(_root, "cache"), canceled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = processor.ProcessImageAsync(source, Path.Combine(_root, "second"), Path.Combine(_root, "cache"));
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
