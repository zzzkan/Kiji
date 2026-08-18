using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="ContentDictionary{T}"/> and the declaration surface that
/// builds it: enumeration order, key validity and uniqueness, item validation, and
/// resolution by element type.
/// </summary>
public sealed class ContentDictionaryTests
{
    private sealed record Item(string Slug, int Order);

    private sealed record Derived(string Slug);

    [Fact]
    public void Dictionary_LooksItemsUpByKeyCaseInsensitively()
    {
        var dictionary = Content.FromItems<Item>([new("b", 2), new("a", 1)], static item => item.Slug);

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
        var dictionary = Content.FromItems<Item>([new("a", 1)], static item => item.Slug);

        Assert.Throws<KeyNotFoundException>(() => dictionary["missing"]);
    }

    /// <summary>
    /// Route sets, and therefore every output path, are derived by enumerating a
    /// dictionary. A stable order is what keeps a rebuild byte-identical.
    /// </summary>
    [Fact]
    public void Enumeration_IsAscendingByKey_RegardlessOfInsertionOrder()
    {
        var dictionary = Content.FromItems<Item>(
            [new("charlie", 1), new("alpha", 2), new("bravo", 3)],
            static item => item.Slug);
        var reversed = Content.FromItems<Item>(
            [new("bravo", 3), new("alpha", 2), new("charlie", 1)],
            static item => item.Slug);

        Assert.Equal(["alpha", "bravo", "charlie"], dictionary.Select(static entry => entry.Key));
        Assert.Equal(dictionary.Select(static entry => entry.Key), reversed.Select(static entry => entry.Key));
        Assert.Equal(["alpha", "bravo", "charlie"], dictionary.Keys);
        Assert.Equal([2, 3, 1], dictionary.Values.Select(static item => item.Order));
    }

    [Fact]
    public void EmptyKey_FailsNamingTheItem()
    {
        var dictionary = Content.FromItems<Item>([new("ok", 1), new("   ", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        Assert.Contains("empty key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateKeys_FailMaterializationNamingBothItems()
    {
        var dictionary = Content.FromItems<Item>([new("same", 1), new("SAME", 2)], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        Assert.Contains("duplicate key", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SAME", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReportsEveryFailureAtOnce()
    {
        var dictionary = Content.FromItems<Item>(
            [new("a", 0), new("b", 1), new("c", 0)],
            static item => item.Slug,
            static options => options.Validate(static item => item.Order > 0, "order must be positive"));

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        // Both failures are reported, so content is not fixed one rebuild at a time.
        Assert.Contains("2 invalid item(s)", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(exception.Message, "order must be positive"));
    }

    [Fact]
    public void Validate_ManyFailures_TruncatesWithRemainderCount()
    {
        var dictionary = Content.FromItems<Item>(
            [.. Enumerable.Range(0, 15).Select(index => new Item($"item-{index}", 0))],
            static item => item.Slug,
            static options => options.Validate(static item => item.Order > 0, "order must be positive"));

        var exception = Assert.Throws<InvalidOperationException>(() => dictionary.Count);

        Assert.Contains("15 invalid item(s)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("and 5 more", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, CountOccurrences(exception.Message, "order must be positive"));
    }

    [Fact]
    public void AddContentSource_SameElementTypeTwice_Throws()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Item>(static _ => [], static item => item.Slug);

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddContentSource<Item>(static _ => [], static item => item.Slug));

        Assert.Contains("already registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(Item), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnregisteredElementType_FailsToResolveNamingTheType()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();

        var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.Services.GetRequiredService<ContentDictionary<Item>>());

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
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Item>(
            static _ => [new("a", 1), new("b", 2)],
            static item => item.Slug);
        builder.AddContentSource<Derived>(
            static services => [.. services.GetRequiredService<ContentDictionary<Item>>().Values.Select(static item => new Derived(item.Slug.ToUpperInvariant()))],
            static derived => derived.Slug);

        var app = builder.Build();

        Assert.Equal(["A", "B"], app.Services.GetRequiredService<ContentDictionary<Derived>>().Keys);
    }

    [Fact]
    public void Dictionary_CircularDerivation_ThrowsNamingThePath()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Item>(
            static services => [.. services.GetRequiredService<ContentDictionary<Derived>>().Values.Select(static d => new Item(d.Slug, 1))],
            static item => item.Slug);
        builder.AddContentSource<Derived>(
            static services => [.. services.GetRequiredService<ContentDictionary<Item>>().Values.Select(static i => new Derived(i.Slug))],
            static derived => derived.Slug);

        var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<ContentDictionary<Item>>().Count);

        Assert.Contains("cycle", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"ContentDictionary<{nameof(Item)}>", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"ContentDictionary<{nameof(Derived)}>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dictionary_ResolvesFromServicesByElementType()
    {
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Item>(static _ => [new("a", 1)], static item => item.Slug);

        var app = builder.Build();

        var dictionary = Assert.IsType<ContentDictionary<Item>>(
            app.Services.GetService(typeof(ContentDictionary<Item>)));
        Assert.Equal("a", Assert.Single(dictionary).Key);
    }

    /// <summary>
    /// Mapping code pulls catalogs out before the build runs. Taking the handle must not
    /// load anything: the dev server rebuilds catalogs on every snapshot, and resolving
    /// <c>SsgOptions</c> this early would hand it the build output path instead of its
    /// serve mirror.
    /// </summary>
    [Fact]
    public void ResolvingTheDictionary_TakesTheHandleWithoutLoading()
    {
        var loads = 0;
        var builder = KijiApp.CreateBuilder([]);
        builder.Site = TestArticleContents.CreateSiteInfo();
        builder.AddContentSource<Item>(
            _ =>
            {
                loads++;
                return [new("a", 1)];
            },
            static item => item.Slug);

        var app = builder.Build();
        var dictionary = app.Services.GetRequiredService<ContentDictionary<Item>>();

        Assert.Equal(0, loads);

        Assert.Single(dictionary);
        Assert.Equal(1, loads);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
