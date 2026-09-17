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
    public async Task TypedRegistration_IsDeferredAndConcatenatesWithoutAssemblyDiscovery()
    {
        await using var site = CreateSite();
        var calls = 0;
        site.AddPages<Parameterized>(_ => { calls++; return [new { Value = "one" }]; });
        site.AddPages<Parameterized>(static _ => []);
        site.AddPages<Parameterized>(_ => { calls++; return [new { Value = "two" }]; });
        Assert.Equal(0, calls);

        var pages = site.CreateSnapshot().Pages;

        Assert.Equal(2, calls);
        Assert.Equal(["/items/one/", "/items/two/"], pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task TypedRegistration_AcceptsStringDictionaryAndConvertsAnonymousValuesInvariantly()
    {
        await using var site = CreateSite();
        site.AddPages<Parameterized>(static _ =>
        [
            new Dictionary<string, string> { ["Value"] = "dictionary" },
            new { Value = 42 },
        ]);

        Assert.Equal(
            ["/items/42/", "/items/dictionary/"],
            site.CreateSnapshot().Pages.Select(static page => page.RoutePath).Order());
    }

    [Fact]
    public async Task TypedRegistration_RejectsNullRouteValue()
    {
        await using var site = CreateSite();
        site.AddPages<Parameterized>(static _ => [new { Value = (string?)null }]);

        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("null or whitespace", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticDiscovery_CombinesAssembliesAndIgnoresRepeatedRegistration()
    {
        var first = CreatePageType("/first/", "/ignored/{Value}/");
        var second = CreatePageType("/second/", "/ignored/{Value}/");
        var parameterizedOnly = CreatePageType("/ignored/{Value}/");
        await using var site = CreateSite();
        site.AddStaticPages(first.Assembly).AddStaticPages(first.Assembly)
            .AddStaticPages(second.Assembly).AddStaticPages(parameterizedOnly.Assembly);

        Assert.Equal(["/first/", "/second/"], site.CreateSnapshot().Pages.Select(page => page.RoutePath).Order());
    }

    [Fact]
    public async Task StaticDiscovery_DuplicateRegisteredRoutesAreRejected()
    {
        await using var site = CreateSite();
        site.AddStaticPages(CreatePageType("/duplicate/").Assembly);
        site.AddStaticPages(CreatePageType("/duplicate/").Assembly);
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Theory]
    [InlineData(typeof(NoRoute), "does not declare")]
    [InlineData(typeof(Fixed), "no dynamic")]
    [InlineData(typeof(MultipleRoutes), "multiple dynamic")]
    public async Task TypedRegistration_RequiresOneParameterizedRoute(Type type, string message)
    {
        await using var site = CreateSite();
        RegisterTyped(site, type);
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedRegistration_OutputCollisionIsRejected()
    {
        await using var site = CreateSite();
        site.AddPages<Parameterized>(static _ => [new { Value = "same" }, new { Value = "same" }]);
        Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotFound_DirectRegistrationReplacesAnyDiscoveredRoute(bool scan)
    {
        var type = CreatePageType("/missing/");
        await using var site = CreateSite();
        if (scan)
        {
            site.AddStaticPages(type.Assembly);
        }
        RegisterNotFound(site, type);

        var page = Assert.Single(site.CreateSnapshot().Pages);
        Assert.Equal("/404.html", page.RoutePath);
        Assert.Equal("404.html", page.OutputRelativePath);
    }

    [Fact]
    public async Task NotFound_MultipleRouteDeclarationsAreRejected()
    {
        await using var site = CreateSite();
        site.UseNotFoundPage<MultipleRoutes>();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_CannotOverwriteDirectoryRequiredByAnotherPage()
    {
        await using var site = CreateSite();
        site.AddStaticPages(CreatePageType("/404.html/nested/").Assembly);
        site.UseNotFoundPage<Fixed>();
        var exception = Assert.Throws<InvalidOperationException>(() => site.CreateSnapshot());
        Assert.Contains("also a page output file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_OriginalRouteCanBelongToAnotherRegisteredPage()
    {
        var notFound = CreatePageType("/missing/");
        await using var site = CreateSite();
        site.AddStaticPages(notFound.Assembly).AddStaticPages(CreatePageType("/missing/").Assembly);
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
            await using var site = CreateSite();
            site.Paths.RootDirectory = root;
            site.UseNotFoundPage<Fixed>();
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
            site.UseNotFoundPage<Parameterized>();
            Assert.Throws<InvalidOperationException>(() => site.AddPages<Parameterized>(static _ => []));
        }
        else
        {
            site.AddPages<Parameterized>(static _ => []);
            Assert.Throws<InvalidOperationException>(() => site.UseNotFoundPage<Parameterized>());
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
            await using var site = CreateSite();
            site.Paths.RootDirectory = root;
            var type = CreatePageType(notFound ? "/missing/" : "/items/{Value}/");
            if (notFound)
            {
                RegisterNotFound(site, type);
            }
            else
            {
                RegisterTyped(site, type);
            }

            await site.PublishAsync("dist");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".kiji", "cache", "build-manifest.json")));
            var identity = $"{type.Assembly.GetName().Name}:{type.Module.ModuleVersionId:N}";
            Assert.Contains(manifest.RootElement.GetProperty("AssemblyMvids").EnumerateArray(), item => item.GetString() == identity);
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
        var typed = PageDiscovery.FromTypes([type]);
        HotReloadHandler.ClearCache([type]);

        Assert.NotSame(scanned, PageDiscovery.FromAssembly(type.Assembly));
        var refreshed = PageDiscovery.FromTypes([type]);
        Assert.NotSame(typed[0], refreshed[0]);
        Assert.Equal(typed.Select(page => page.SourceIdentifier), refreshed.Select(page => page.SourceIdentifier));
    }

    private static StaticSite CreateSite()
    {
        var site = StaticSite.Create([]);
        site.Info = TestArticleContents.CreateSiteInfo();
        return site;
    }

    private static void RegisterTyped(StaticSite site, Type type)
    {
        typeof(StaticSite).GetMethod(nameof(StaticSite.AddPages))!.MakeGenericMethod(type)
            .Invoke(site, [new Func<IServiceProvider, IEnumerable<object>>(static _ => [])]);
    }

    private static void RegisterNotFound(StaticSite site, Type type)
    {
        typeof(StaticSite).GetMethod(nameof(StaticSite.UseNotFoundPage))!.MakeGenericMethod(type).Invoke(site, null);
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
    private sealed class MultipleRoutes : ComponentBase;

    private sealed class NoRoute : ComponentBase;
}
