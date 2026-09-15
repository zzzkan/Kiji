using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class HashConcurrencyTests
{
    [Fact]
    public async Task ConcurrentFileMissesComputeOnce_AndNextBuildRetriesMissingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kiji-file-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "missing.md");
            var count = 0;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            IncrementalBuildPlanner Create()
            {
                return new IncrementalBuildPlanner(new ResolvedSitePaths
                {
                    ContentDirectory = root,
                    OutputDirectory = root,
                    StaticDirectory = root,
                }, root, root, new SiteInfo { BaseUrl = new Uri("https://example.com/"), Name = "Test" }, []);
            }
            var planner = new IncrementalBuildPlanner(new ResolvedSitePaths
            {
                ContentDirectory = root,
                OutputDirectory = root,
                StaticDirectory = root,
            }, root, root, new SiteInfo { BaseUrl = new Uri("https://example.com/"), Name = "Test" }, [])
            {
                BeforeFileHash = _ => { Interlocked.Increment(ref count); entered.Set(); release.Wait(); },
            };
            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() => planner.HashFileCached(path))).ToArray();
            try { Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); }
            finally { release.Set(); }
            var hashes = await Task.WhenAll(tasks);
            Assert.All(hashes, hash => Assert.Equal(BuildFingerprint.Missing, hash));
            Assert.Equal(1, count);
            File.WriteAllText(path, "created for the next build");
            Assert.Equal(BuildFingerprint.HashFile(path), Create().HashFileCached(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConcurrentRequestsComputeEachScopeAndFileOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kiji-concurrent-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "post.md");
            File.WriteAllText(path, "example");
            var scopeCount = 0;
            var fileCount = 0;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var planner = new IncrementalBuildPlanner(new ResolvedSitePaths
            {
                ContentDirectory = root,
                OutputDirectory = root,
                StaticDirectory = root,
            }, root, root, new SiteInfo { BaseUrl = new Uri("https://example.com/"), Name = "Test" }, [])
            {
                BeforeContentSetHash = _ => { Interlocked.Increment(ref scopeCount); entered.Set(); release.Wait(); },
                BeforeFileHash = _ => Interlocked.Increment(ref fileCount),
            };
            var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() => planner.ContentSetFingerprint(""))).ToArray();
            try { Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); }
            finally { release.Set(); }
            var hashes = await Task.WhenAll(tasks);
            Assert.All(hashes, hash => Assert.Equal(hashes[0], hash));
            Assert.Equal(1, scopeCount);
            Assert.Equal(1, fileCount);
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => planner.HashFileCached(path))));
            Assert.Equal(1, fileCount);
        }
        finally { Directory.Delete(root, true); }
    }
}
