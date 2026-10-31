using Microsoft.Extensions.DependencyInjection;
using Kiji.Generation;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class ContentCollectionTests
{
    private sealed record Item(string Slug, int Order);

    [Fact]
    public async Task ConcurrentReaders_MaterializeContentOnlyOnce()
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        var calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        builder.RegisterContent<Item>(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return [("shared", new Item("shared", 1), "stable")];
        });
        await using var app = builder.Build();
        var collection = app.ServiceProvider.GetRequiredService<ContentCollection<Item>>();
        var first = Task.Run(() => collection["shared"]);
        Task<Item>? second = null;
        using var secondStarted = new ManualResetEventSlim();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            second = Task.Run(() => { secondStarted.Set(); return collection["shared"]; });
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(10)));
        }
        finally { release.Set(); }
        var results = await Task.WhenAll(first, second!);
        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
        app.InvalidateContent();
        Assert.NotSame(results[0], collection["shared"]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Collection_LooksItemsUpByIdCaseInsensitively()
    {
        var collection = ContentCollectionFixture.FromItems<Item>([new("b", 2), new("a", 1)], static item => item.Slug);

        Assert.Equal(2, collection.Count);
        Assert.Equal(2, collection["b"].Order);
        Assert.True(collection.TryGetValue("A", out var found));
        Assert.Equal(1, found.Order);
        Assert.True(collection.Contains("B"));
        Assert.False(collection.Contains("missing"));
        Assert.Throws<KeyNotFoundException>(() => collection["missing"]);
    }

    /// <summary>
    /// Route sets, and therefore every output path, are derived by enumerating a
    /// collection. A stable order is what keeps a rebuild byte-identical.
    /// </summary>
    [Fact]
    public void Enumeration_PreservesLoaderOrder()
    {
        var collection = ContentCollectionFixture.FromItems<Item>(
            [new("charlie", 1), new("alpha", 2), new("bravo", 3)],
            static item => item.Slug);

        Assert.Equal(["charlie", "alpha", "bravo"], collection.Select(static entry => entry.Slug));
        Assert.Equal([1, 2, 3], collection.Select(static item => item.Order));
    }

    [Fact]
    public void EmptyId_FailsNamingTheItem()
    {
        var collection = ContentCollectionFixture.FromItems<Item>([new("ok", 1), new("   ", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => collection.Count);

        Assert.Contains("empty ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateIds_FailMaterializationNamingBothItems()
    {
        var collection = ContentCollectionFixture.FromItems<Item>([new("same", 1), new("SAME", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => collection.Count);

        Assert.Contains("duplicate ID", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SAME", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mapping code pulls catalogs out before the build runs. Taking the handle must not
    /// load anything: the dev server rebuilds catalogs on every snapshot, and resolving
    /// <c>ResolvedSitePaths</c> this early would hand it the build output path instead of its
    /// serve mirror.
    /// </summary>
    [Fact]
    public void ResolvingTheCollection_TakesTheHandleWithoutLoading()
    {
        var loads = 0;
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        builder.RegisterContent<Item>(
            _ =>
            {
                loads++;
                return [("a", new Item("a", 1), "stable")];
            });
        var app = builder.Build();
        var collection = app.ServiceProvider.GetRequiredService<ContentCollection<Item>>();

        Assert.Equal(0, loads);

        Assert.Single(collection);
        Assert.Equal(1, loads);
    }

    [Theory]
    [InlineData("enumerate", false)]
    [InlineData("count", false)]
    [InlineData("missing", false)]
    [InlineData("lookup", true)]
    [InlineData("contains", true)]
    public async Task Reads_RecordCollectionOrItemDependencies(string operation, bool itemDependency)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        builder.RegisterContent<Item>(_ => [("one", new Item("one", 1), "first")]);
        await using var site = builder.Build();
        var collection = site.ServiceProvider.GetRequiredService<ContentCollection<Item>>();
        var recorder = new BuildDependencyRecorder();
        PageRenderContext.SetCurrent(new PageRenderContext
        {
            RoutePath = "/",
            OutputRelativeDirectory = "",
            OutputUrlDirectory = "/",
            Dependencies = recorder,
        });
        try
        {
            switch (operation)
            {
                case "enumerate": _ = collection.ToArray(); break;
                case "count": _ = collection.Count; break;
                case "missing": _ = collection.Contains("missing"); break;
                case "lookup": _ = collection["ONE"]; break;
                case "contains": _ = collection.Contains("one"); break;
            }
        }
        finally { PageRenderContext.SetCurrent(null); }
        var dependency = Assert.Single(recorder.Values);
        Assert.Equal(itemDependency, dependency.Key.EndsWith("/ONE", StringComparison.Ordinal));
    }

}
