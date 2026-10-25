using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Kiji.Hosting;
using Kiji.Routing;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Kiji.Tests;

public sealed class PageRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignPage_RequiresExplicitAssemblyRegistration(bool notFound)
    {
        var type = CreatePageType("/foreign/");
        var builder = CreateBuilder();
        if (notFound) { RegisterNotFound(builder, type); }
        else { RegisterTyped(builder, type); }
        await using var site = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("AddPageAssembly", error.Message, StringComparison.Ordinal);
        Assert.Contains(type.FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageParameters_AreDeferredAndConcatenated()
    {
        var builder = CreateBuilder();
        var calls = 0;
        builder.AddPageParameters<Parameterized>(_ => { calls++; return [new { Value = "one" }]; });
        builder.AddPageParameters<Parameterized>(static _ => []);
        builder.AddPageParameters<Parameterized>(_ => { calls++; return [new { Value = "two" }]; });
        Assert.Equal(0, calls);

        await using var site = builder.Build();
        var pages = site.CreateSnapshot().Pages;

        Assert.Equal(2, calls);
        Assert.Equal(["/items/one/", "/items/two/"], pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task TypedRegistration_AcceptsStringDictionaryAndAnonymousStrings()
    {
        var builder = CreateBuilder();
        builder.AddPageParameters<Parameterized>(static _ =>
        [
            new Dictionary<string, string> { ["Value"] = "dictionary" },
            new { Value = "42" },
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["Value"] = "readonly" }),
        ]);

        await using var site = builder.Build();
        Assert.Equal(
            ["/items/42/", "/items/dictionary/", "/items/readonly/"],
            site.CreateSnapshot().Pages.Select(static page => page.RoutePath).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task TypedRegistration_RejectsNullOrWhitespaceRouteValue(string? value)
    {
        var builder = CreateBuilder();
        builder.AddPageParameters<Parameterized>(_ => [new { Value = value }]);

        await using var site = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("null or whitespace", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RouteName_AlsoRequiresADeclaredParameter()
    {
        var builder = CreateBuilder();
        var type = CreatePageType("/missing/{Value}/");
        builder.AddPageAssembly(type.Assembly);
        RegisterTyped(builder, type, new { Value = "one" });
        await using var site = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("'Value'", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a declared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_CombinesAssembliesAndIgnoresRepeatedRegistration()
    {
        var first = CreatePageType("/first/");
        var second = CreatePageType("/second/");
        var builder = CreateBuilder();
        builder.AddPageAssembly(first.Assembly).AddPageAssembly(first.Assembly).AddPageAssembly(second.Assembly);

        await using var site = builder.Build();
        Assert.Equal(["/first/", "/second/"], site.CreateSnapshot().Pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task StaticDiscovery_DuplicateRegisteredRoutesAreRejected()
    {
        var builder = CreateBuilder();
        builder.AddPageAssembly(CreatePageType("/duplicate/").Assembly);
        builder.AddPageAssembly(CreatePageType("/duplicate/").Assembly);
        await using var site = builder.Build();
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Fact]
    public async Task MixedRoutes_ParametersTargetTheParameterizedRoute()
    {
        var builder = CreateBuilder();
        builder.AddPageParameters<MixedRoutes>(static _ => [new { Value = "one" }]);

        await using var site = builder.Build();
        var pages = site.CreateSnapshot().Pages.OrderBy(static page => page.RoutePath, StringComparer.Ordinal).ToArray();
        Assert.Equal(["/mixed/", "/mixed/one/"], pages.Select(static page => page.RoutePath));
        Assert.Empty(pages[0].Parameters);
        Assert.Equal("one", pages[1].Parameters["Value"]);
    }

    [Theory]
    [InlineData(typeof(MultipleRoutes), "/one/a/", "/two/a/")]
    [InlineData(typeof(MultipleFixedRoutes), "/one/", "/two/")]
    public async Task MultipleRoutes_RenderEachParameterSetAtEveryReceivingRoute(Type type, string first, string second)
    {
        var builder = CreateBuilder();
        RegisterTyped(builder, type, new { Value = "a" });

        await using var site = builder.Build();
        var pages = site.CreateSnapshot().Pages;
        Assert.Equal([first, second], pages.Select(static page => page.RoutePath).Order(StringComparer.Ordinal));
        Assert.All(pages, static page => Assert.Equal("a", page.Parameters["Value"]));
    }

    [Fact]
    public async Task MultipleParameterizedRoutes_RequireEveryRouteValue()
    {
        var builder = CreateBuilder();
        builder.AddPageParameters<DistinctRouteValues>(static _ => [new { First = "x" }]);
        await using var site = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("'/second/{Second}/'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Second'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageParameters_RequireARoute()
    {
        var builder = CreateBuilder();
        RegisterTyped(builder, typeof(NoRoute));
        await using var site = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("does not declare", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedRegistration_OutputCollisionIsRejected()
    {
        var builder = CreateBuilder();
        builder.AddPageParameters<Parameterized>(static _ => [new { Value = "same" }, new { Value = "same" }]);
        await using var site = builder.Build();
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Fact]
    public async Task NotFound_MultipleRouteDeclarationsAreRejected()
    {
        var builder = CreateBuilder();
        builder.AddNotFoundPage<MultipleRoutes>();
        await using var site = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_CannotOverwriteDirectoryRequiredByAnotherPage()
    {
        var builder = CreateBuilder();
        builder.AddPageAssembly(CreatePageType("/404.html/nested/").Assembly);
        builder.AddNotFoundPage<Fixed>();
        await using var site = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("also a page output file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_OriginalRouteCanBelongToAnotherRegisteredPage()
    {
        var notFound = CreatePageType("/missing/");
        var builder = CreateBuilder();
        builder.AddPageAssembly(notFound.Assembly).AddPageAssembly(CreatePageType("/missing/").Assembly);
        RegisterNotFound(builder, notFound);
        await using var site = builder.Build();
        Assert.Equal(["/404.html", "/missing/"], site.CreateSnapshot().Pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task NotFound_OutputCollisionIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KijiNotFound_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
            await File.WriteAllTextAsync(Path.Combine(root, "wwwroot", "404.html"), "existing");
            var builder = CreateBuilder(new SiteExecutionPaths(root, Path.Combine(root, "obj", "site")));
            TestSite.TestSiteAssets.Bind(builder.Paths, Path.Combine(root, "wwwroot"));
            builder.AddNotFoundPage<Fixed>();
            await using var site = builder.Build();
            await Assert.ThrowsAsync<InvalidOperationException>(() => site.PublishAsync("dist"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotFound_CannotAlsoBeRegisteredWithParameters(bool notFoundFirst)
    {
        var builder = CreateBuilder();
        if (notFoundFirst)
        {
            builder.AddNotFoundPage<Parameterized>();
            Assert.Throws<InvalidOperationException>(() => builder.AddPageParameters<Parameterized>(static _ => []));
        }
        else
        {
            builder.AddPageParameters<Parameterized>(static _ => []);
            Assert.Throws<InvalidOperationException>(() => builder.AddNotFoundPage<Parameterized>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectPageAssembly_ParticipatesInPublishedFingerprint(bool notFound)
    {
        var root = Path.Combine(Path.GetTempPath(), $"KijiRegistration_{Guid.NewGuid():N}");
        try
        {
            var builder = CreateBuilder(new SiteExecutionPaths(root));
            var type = CreatePageType(notFound ? "/missing/" : "/items/{Value}/");
            if (notFound)
            {
                RegisterNotFound(builder, type);
            }
            else
            {
                RegisterTyped(builder, type);
            }
            builder.AddPageAssembly(type.Assembly);

            await using var site = builder.Build();
            await site.PublishAsync("dist");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".kiji", "cache", "manifest.json")));
            var identity = $"{type.Assembly.GetName().Name}:unavailable";
            Assert.Contains(manifest.RootElement.GetProperty("CodeDependencies").EnumerateArray(), item => item.GetString() == identity);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void HotReload_DropsAssemblyAndTypedRouteMetadata()
    {
        var type = CreatePageType("/fixed/", "/items/{Value}/");
        var scanned = PageDiscovery.FromAssembly(type.Assembly);
        var typed = PageDiscovery.FromType(type);
        HotReloadHandler.ClearCache([type]);

        Assert.NotSame(scanned, PageDiscovery.FromAssembly(type.Assembly));
        var refreshed = PageDiscovery.FromType(type);
        Assert.NotSame(typed[0], refreshed[0]);
        Assert.Equal(typed.Select(page => page.SourceIdentifier), refreshed.Select(page => page.SourceIdentifier));
    }

    private static StaticSiteBuilder CreateBuilder(SiteExecutionPaths? paths = null)
    {
        var builder = TestSiteFactory.CreateBuilder(paths ?? new SiteExecutionPaths(AppContext.BaseDirectory));
        builder.SiteInfo = TestArticleContents.CreateSiteInfo();
        return builder;
    }

    private static void RegisterTyped(StaticSiteBuilder builder, Type type, params object[] parameters)
    {
        typeof(StaticSiteBuilder).GetMethod(nameof(StaticSiteBuilder.AddPageParameters), 1, [typeof(Func<IServiceProvider, IEnumerable<object>>)])!.MakeGenericMethod(type)
            .Invoke(builder, [new Func<IServiceProvider, IEnumerable<object>>(_ => parameters)]);
    }

    private static void RegisterNotFound(StaticSiteBuilder builder, Type type)
    {
        typeof(StaticSiteBuilder).GetMethod(nameof(StaticSiteBuilder.AddNotFoundPage))!.MakeGenericMethod(type).Invoke(builder, null);
    }

    private static Type CreatePageType(params string[] routes)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"KijiPage_{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Pages");
        var attribute = new CustomAttributeBuilder(typeof(RouteAttribute).GetConstructor([typeof(string)])!, ["/excluded/"]);
        var hidden = module.DefineType("Hidden", TypeAttributes.NotPublic, typeof(ComponentBase));
        hidden.SetCustomAttribute(attribute);
        hidden.CreateType();
        var abstractPage = module.DefineType("AbstractPage", TypeAttributes.Public | TypeAttributes.Abstract, typeof(ComponentBase));
        abstractPage.SetCustomAttribute(attribute);
        abstractPage.CreateType();
        var nonComponent = module.DefineType("NonComponent", TypeAttributes.Public);
        nonComponent.SetCustomAttribute(attribute);
        nonComponent.CreateType();
        var type = module.DefineType("Page", TypeAttributes.Public, typeof(ComponentBase));
        foreach (var route in routes)
        {
            type.SetCustomAttribute(new CustomAttributeBuilder(typeof(RouteAttribute).GetConstructor([typeof(string)])!, [route]));
        }
        return type.CreateType()!;
    }

    [Route("/items/{Value}/")]
    private sealed class Parameterized : ComponentBase
    {
        [Parameter] public string Value { get; set; } = string.Empty;
    }

    [Route("/fixed/")]
    private sealed class Fixed : ComponentBase;

    [Route("/one/{Value}/"), Route("/two/{Value}/")]
    private sealed class MultipleRoutes : ComponentBase
    {
        [Parameter] public string Value { get; set; } = string.Empty;
    }

    [Route("/one/"), Route("/two/")]
    private sealed class MultipleFixedRoutes : ComponentBase
    {
        [Parameter] public string Value { get; set; } = string.Empty;
    }

    [Route("/first/{First}/"), Route("/second/{Second}/")]
    private sealed class DistinctRouteValues : ComponentBase
    {
        [Parameter] public string First { get; set; } = string.Empty;
        [Parameter] public string Second { get; set; } = string.Empty;
    }

    [Route("/mixed/"), Route("/mixed/{Value}/")]
    private sealed class MixedRoutes : ComponentBase
    {
        [Parameter] public string Value { get; set; } = string.Empty;
    }

    private sealed class NoRoute : ComponentBase;
}
