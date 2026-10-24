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
    [Fact]
    public async Task EntryAssembly_IsDiscoveredOnce_WithoutRegistration()
    {
        var type = CreatePageType("/entry/", "/alias/");
        await using var site = StaticSite.Create([], new SiteExecutionPaths(AppContext.BaseDirectory), type.Assembly);
        site.Info = TestArticleContents.CreateSiteInfo();
        site.AddPageAssembly(type.Assembly);
        Assert.Equal(["/alias/", "/entry/"], site.CreateSnapshot().Pages.Select(page => page.RoutePath).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignPage_RequiresExplicitAssemblyRegistration(bool notFound)
    {
        var type = CreatePageType("/foreign/");
        await using var site = CreateSite();
        if (notFound) { RegisterNotFound(site, type); }
        else { RegisterTyped(site, type); }
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("AddPageAssembly", error.Message, StringComparison.Ordinal);
        Assert.Contains(type.FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignParameters_CanPrecedeAssemblyRegistration()
    {
        var type = CreatePageType("/foreign/{Value}/");
        await using var site = CreateSite();
        RegisterTyped(site, type);
        site.AddPageAssembly(type.Assembly);
        Assert.Empty(site.CreateSnapshot().Pages);
    }

    [Fact]
    public async Task AdditionalAssembly_DynamicRouteWithoutParametersFails()
    {
        var type = CreatePageType("/foreign/{Value}/");
        await using var site = CreateSite();
        site.AddPageAssembly(type.Assembly);
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("/foreign/{Value}/", error.Message, StringComparison.Ordinal);
        Assert.Contains("AddPageParameters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageParameters_AreDeferredAndConcatenated()
    {
        await using var site = CreateSite();
        var calls = 0;
        site.AddPageParameters<Parameterized>(_ => { calls++; return [new { Value = "one" }]; });
        site.AddPageParameters<Parameterized>(static _ => []);
        site.AddPageParameters<Parameterized>(_ => { calls++; return [new { Value = "two" }]; });
        Assert.Equal(0, calls);

        var pages = site.CreateSnapshot().Pages;

        Assert.Equal(2, calls);
        Assert.Equal(["/items/one/", "/items/two/"], pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task TypedRegistration_AcceptsStringDictionaryAndAnonymousStrings()
    {
        await using var site = CreateSite();
        site.AddPageParameters<Parameterized>(static _ =>
        [
            new Dictionary<string, string> { ["Value"] = "dictionary" },
            new { Value = "42" },
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["Value"] = "readonly" }),
        ]);

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
        await using var site = CreateSite();
        site.AddPageParameters<Parameterized>(_ => [new { Value = value }]);

        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("null or whitespace", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RouteName_AlsoRequiresADeclaredParameter()
    {
        await using var site = CreateSite();
        var type = CreatePageType("/missing/{Value}/");
        site.AddPageAssembly(type.Assembly);
        typeof(StaticSite).GetMethod(nameof(StaticSite.AddPageParameters), 1, [typeof(Func<IServiceProvider, IEnumerable<object>>)])!.MakeGenericMethod(type)
            .Invoke(site, [new Func<IServiceProvider, IEnumerable<object>>(static _ => [new { Value = "one" }])]);
        var error = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("'Value'", error.Message, StringComparison.Ordinal);
        Assert.Contains("not a declared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_CombinesAssembliesAndIgnoresRepeatedRegistration()
    {
        var first = CreatePageType("/first/");
        var second = CreatePageType("/second/");
        var parameterizedOnly = CreatePageType("/empty/{Value}/");
        await using var site = CreateSite();
        site.AddPageAssembly(first.Assembly).AddPageAssembly(first.Assembly)
            .AddPageAssembly(second.Assembly).AddPageAssembly(parameterizedOnly.Assembly);
        RegisterTyped(site, parameterizedOnly);

        Assert.Equal(["/first/", "/second/"], site.CreateSnapshot().Pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task StaticDiscovery_DuplicateRegisteredRoutesAreRejected()
    {
        await using var site = CreateSite();
        site.AddPageAssembly(CreatePageType("/duplicate/").Assembly);
        site.AddPageAssembly(CreatePageType("/duplicate/").Assembly);
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedRoutes_ParametersTargetTheParameterizedRoute(bool discovered)
    {
        // Discovered: a public scanned component. Otherwise: a non-public component included by registration.
        var type = discovered ? CreatePageType(valueParameter: true, "/mixed/", "/mixed/{Value}/") : typeof(MixedRoutes);
        await using var site = CreateSite();
        if (discovered) { site.AddPageAssembly(type.Assembly); }
        RegisterTyped(site, type, new { Value = "one" });

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
        await using var site = CreateSite();
        RegisterTyped(site, type, new { Value = "a" });

        var pages = site.CreateSnapshot().Pages;
        Assert.Equal([first, second], pages.Select(static page => page.RoutePath).Order(StringComparer.Ordinal));
        Assert.All(pages, static page => Assert.Equal("a", page.Parameters["Value"]));
    }

    [Fact]
    public async Task MultipleParameterizedRoutes_RequireEveryRouteValue()
    {
        await using var site = CreateSite();
        site.AddPageParameters<DistinctRouteValues>(static _ => [new { First = "x" }]);
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("'/second/{Second}/'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Second'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageParameters_RequireARoute()
    {
        await using var site = CreateSite();
        RegisterTyped(site, typeof(NoRoute));
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("does not declare", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedRegistration_OutputCollisionIsRejected()
    {
        await using var site = CreateSite();
        site.AddPageParameters<Parameterized>(static _ => [new { Value = "same" }, new { Value = "same" }]);
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotFound_AssemblyRegistrationIsOrderIndependent(bool scan)
    {
        var type = CreatePageType("/missing/");
        await using var site = CreateSite();
        if (scan)
        {
            site.AddPageAssembly(type.Assembly);
        }
        RegisterNotFound(site, type);
        if (!scan) { site.AddPageAssembly(type.Assembly); }

        var page = Assert.Single(site.CreateSnapshot().Pages);
        Assert.Equal("/404.html", page.RoutePath);
        Assert.Equal("404.html", page.OutputRelativePath);
    }

    [Fact]
    public async Task NotFound_MultipleRouteDeclarationsAreRejected()
    {
        await using var site = CreateSite();
        site.AddNotFoundPage<MultipleRoutes>();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_CannotOverwriteDirectoryRequiredByAnotherPage()
    {
        await using var site = CreateSite();
        site.AddPageAssembly(CreatePageType("/404.html/nested/").Assembly);
        site.AddNotFoundPage<Fixed>();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("also a page output file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_OriginalRouteCanBelongToAnotherRegisteredPage()
    {
        var notFound = CreatePageType("/missing/");
        await using var site = CreateSite();
        site.AddPageAssembly(notFound.Assembly).AddPageAssembly(CreatePageType("/missing/").Assembly);
        RegisterNotFound(site, notFound);
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
            await using var site = CreateSite(new SiteExecutionPaths(root, Path.Combine(root, "obj", "site")));
            TestSite.TestSiteAssets.Bind(site, Path.Combine(root, "wwwroot"));
            site.AddNotFoundPage<Fixed>();
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
        await using var site = CreateSite();
        if (notFoundFirst)
        {
            site.AddNotFoundPage<Parameterized>();
            Assert.Throws<InvalidOperationException>(() => site.AddPageParameters<Parameterized>(static _ => []));
        }
        else
        {
            site.AddPageParameters<Parameterized>(static _ => []);
            Assert.Throws<InvalidOperationException>(() => site.AddNotFoundPage<Parameterized>());
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
            await using var site = CreateSite(new SiteExecutionPaths(root));
            var type = CreatePageType(notFound ? "/missing/" : "/items/{Value}/");
            if (notFound)
            {
                RegisterNotFound(site, type);
            }
            else
            {
                RegisterTyped(site, type);
            }
            site.AddPageAssembly(type.Assembly);

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

    private static StaticSite CreateSite(SiteExecutionPaths? paths = null)
    {
        var site = TestSiteFactory.Create([], paths ?? new SiteExecutionPaths(AppContext.BaseDirectory));
        site.Info = TestArticleContents.CreateSiteInfo();
        return site;
    }

    private static void RegisterTyped(StaticSite site, Type type, params object[] parameters)
    {
        typeof(StaticSite).GetMethod(nameof(StaticSite.AddPageParameters), 1, [typeof(Func<IServiceProvider, IEnumerable<object>>)])!.MakeGenericMethod(type)
            .Invoke(site, [new Func<IServiceProvider, IEnumerable<object>>(_ => parameters)]);
    }

    private static void RegisterNotFound(StaticSite site, Type type)
    {
        typeof(StaticSite).GetMethod(nameof(StaticSite.AddNotFoundPage))!.MakeGenericMethod(type).Invoke(site, null);
    }

    private static Type CreatePageType(params string[] routes) => CreatePageType(valueParameter: false, routes);

    private static Type CreatePageType(bool valueParameter, params string[] routes)
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
        if (valueParameter)
        {
            // Planning only reads the [Parameter] declaration and its public setter.
            var setter = type.DefineMethod("set_Value", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null, [typeof(string)]);
            setter.GetILGenerator().Emit(OpCodes.Ret);
            var property = type.DefineProperty("Value", PropertyAttributes.None, typeof(string), null);
            property.SetSetMethod(setter);
            property.SetCustomAttribute(new CustomAttributeBuilder(typeof(ParameterAttribute).GetConstructor(Type.EmptyTypes)!, []));
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
