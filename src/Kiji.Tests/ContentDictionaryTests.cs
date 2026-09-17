using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

public sealed class ContentDictionaryTests
{
    private sealed record Item(string Slug, int Order);

    private sealed record Derived(string Slug);

    [Fact]
    public async Task ConcurrentReaders_MaterializeContentOnlyOnce()
    {
        await using var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        var calls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        app.UseContentSource<Item>(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return [new Item("shared", 1)];
        });
        var dictionary = app.ServiceProvider.GetRequiredService<ContentDictionary<Item>>();
        var first = Task.Run(() => dictionary["0"]);
        Task<Item>? second = null;
        using var secondStarted = new ManualResetEventSlim();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            second = Task.Run(() => { secondStarted.Set(); return dictionary["0"]; });
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(10)));
        }
        finally { release.Set(); }
        var results = await Task.WhenAll(first, second!);
        Assert.Equal(1, calls);
        Assert.Same(results[0], results[1]);
        app.InvalidateContent();
        Assert.NotSame(results[0], dictionary["0"]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dictionary_LooksItemsUpByKeyCaseInsensitively()
    {
        var dictionary = ContentDictionaryFixture.FromItems<Item>([new("b", 2), new("a", 1)], static item => item.Slug);

        Assert.Equal(2, dictionary.Count);
        Assert.Equal(2, dictionary["b"].Order);
        Assert.True(dictionary.TryGetValue("A", out var found));
        Assert.Equal(1, found.Order);
        Assert.True(dictionary.ContainsKey("B"));
        Assert.False(dictionary.ContainsKey("missing"));
    }

    [Fact]
    public void Indexer_UnknownKey_Throws()
    {
        var dictionary = ContentDictionaryFixture.FromItems<Item>([new("a", 1)], static item => item.Slug);

        Assert.Throws<KeyNotFoundException>(() => dictionary["missing"]);
    }

    /// <summary>
    /// Route sets, and therefore every output path, are derived by enumerating a
    /// dictionary. A stable order is what keeps a rebuild byte-identical.
    /// </summary>
    [Fact]
    public void Enumeration_PreservesLoaderOrder()
    {
        var dictionary = ContentDictionaryFixture.FromItems<Item>(
            [new("charlie", 1), new("alpha", 2), new("bravo", 3)],
            static item => item.Slug);

        Assert.Equal(["charlie", "alpha", "bravo"], dictionary.Select(static entry => entry.Key));
        Assert.Equal(["charlie", "alpha", "bravo"], dictionary.Keys);
        Assert.Equal([1, 2, 3], dictionary.Values.Select(static item => item.Order));
    }

    [Fact]
    public void EmptyKey_FailsNamingTheItem()
    {
        var dictionary = ContentDictionaryFixture.FromItems<Item>([new("ok", 1), new("   ", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        Assert.Contains("empty key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateKeys_FailMaterializationNamingBothItems()
    {
        var dictionary = ContentDictionaryFixture.FromItems<Item>([new("same", 1), new("SAME", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        Assert.Contains("duplicate key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SAME", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseContentSource_AssignsOpaqueOrdinalKeysInLoaderOrder()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Item>(static _ => [new("b", 2), new("a", 1)]);
        var dictionary = app.ServiceProvider.GetRequiredService<ContentDictionary<Item>>();

        Assert.Equal(["0", "1"], dictionary.Keys);
        Assert.Equal(["b", "a"], dictionary.Values.Select(static item => item.Slug));
        Assert.Equal("b", dictionary["0"].Slug);
    }

    [Fact]
    public void UseContentSource_SameElementTypeTwice_Throws()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Item>(static _ => []);

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.UseContentSource<Item>(static _ => []));

        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Item), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dictionary's loader receives the app's services, so one dictionary can be built from
    /// another — a tag list over posts, say. This is the supported way to share a single
    /// read across several derived views.
    /// </summary>
    [Fact]
    public void Dictionary_CanBeDerivedFromAnother()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Item>(static _ => [new("a", 1), new("b", 2)]);
        app.UseContentSource<Derived>(
            static services => [.. services.GetRequiredService<ContentDictionary<Item>>().Values.Select(static item => new Derived(item.Slug.ToUpperInvariant()))]);

        var derived = app.ServiceProvider.GetRequiredService<ContentDictionary<Derived>>();
        Assert.Equal(["0", "1"], derived.Keys);
        Assert.Equal(["A", "B"], derived.Values.Select(static item => item.Slug));
    }

    [Fact]
    public void Dictionary_CircularDerivation_ThrowsNamingThePath()
    {
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Item>(
            static services => [.. services.GetRequiredService<ContentDictionary<Derived>>().Values.Select(static d => new Item(d.Slug, 1))]);
        app.UseContentSource<Derived>(
            static services => [.. services.GetRequiredService<ContentDictionary<Item>>().Values.Select(static i => new Derived(i.Slug))]);

        var exception = Assert.Throws<InvalidOperationException>(() => app.ServiceProvider.GetRequiredService<ContentDictionary<Item>>().Count);

        Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"ContentDictionary<{nameof(Item)}>", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"ContentDictionary<{nameof(Derived)}>", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mapping code pulls catalogs out before the build runs. Taking the handle must not
    /// load anything: the dev server rebuilds catalogs on every snapshot, and resolving
    /// <c>ResolvedSitePaths</c> this early would hand it the build output path instead of its
    /// serve mirror.
    /// </summary>
    [Fact]
    public void ResolvingTheDictionary_TakesTheHandleWithoutLoading()
    {
        var loads = 0;
        var app = StaticSite.Create([]);
        app.Info = TestArticleContents.CreateSiteInfo();
        app.UseContentSource<Item>(
            _ =>
            {
                loads++;
                return [new("a", 1)];
            });
        var dictionary = app.ServiceProvider.GetRequiredService<ContentDictionary<Item>>();

        Assert.Equal(0, loads);

        Assert.Single(dictionary);
        Assert.Equal(1, loads);
    }

}
