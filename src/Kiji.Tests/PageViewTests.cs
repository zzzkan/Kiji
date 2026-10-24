using Kiji.Components;
using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class PageViewTests
{
    [Fact]
    public async Task LayoutAttributeOnPage_OverridesDefaultLayout()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PageWithAltLayout), defaultLayout: typeof(MainLayout)));

        Assert.Contains("alt-layout", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-header", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedLayouts_RenderInsideParentLayout()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PageWithNestedLayout), defaultLayout: null));

        var outerIndex = html.IndexOf("outer-layout", StringComparison.Ordinal);
        var innerIndex = html.IndexOf("inner-layout", StringComparison.Ordinal);
        var bodyIndex = html.IndexOf("nested-page-body", StringComparison.Ordinal);
        Assert.True(outerIndex >= 0 && innerIndex > outerIndex && bodyIndex > innerIndex);
    }

    [Fact]
    public async Task CircularLayoutChain_ThrowsInsteadOfLooping()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderComponentAsync<KijiRoot>(
                CreateRootParameters(typeof(PageWithCircularLayout), defaultLayout: null)));
    }

    [Fact]
    public async Task PageWithoutLayoutOrHead_RendersOnlyItsBody()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PlainPage), defaultLayout: null));

        Assert.Contains("<head></head>", html, StringComparison.Ordinal);
        Assert.Contains("plain-page", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-header", html, StringComparison.Ordinal);
    }

    private static StaticSite CreateApp()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        var app = TestSiteFactory.Create(new SiteExecutionPaths(AppContext.BaseDirectory));
        app.Info = siteInfo;
        return app;
    }

    private static Dictionary<string, object?> CreateRootParameters(Type pageType, Type? defaultLayout)
    {
        return new Dictionary<string, object?>
        {
            [nameof(KijiRoot.PageType)] = pageType,
            [nameof(KijiRoot.PageParameters)] = new Dictionary<string, object?>(),
            [nameof(KijiRoot.DefaultLayout)] = defaultLayout,
        };
    }

    private sealed class AltLayout : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "alt-layout");
            builder.AddContent(2, Body);
            builder.CloseElement();
        }
    }

    [Layout(typeof(AltLayout))]
    private sealed class PageWithAltLayout : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, "alt page body");
        }
    }

    private sealed class OuterLayout : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "outer-layout");
            builder.AddContent(2, Body);
            builder.CloseElement();
        }
    }

    [Layout(typeof(OuterLayout))]
    private sealed class InnerLayout : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "inner-layout");
            builder.AddContent(2, Body);
            builder.CloseElement();
        }
    }

    [Layout(typeof(InnerLayout))]
    private sealed class PageWithNestedLayout : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "class", "nested-page-body");
            builder.AddContent(2, "nested");
            builder.CloseElement();
        }
    }

    [Layout(typeof(SelfReferencingLayout))]
    private sealed class SelfReferencingLayout : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, Body);
        }
    }

    [Layout(typeof(SelfReferencingLayout))]
    private sealed class PageWithCircularLayout : ComponentBase
    {
    }

    private sealed class PlainPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "class", "plain-page");
            builder.AddContent(2, "plain");
            builder.CloseElement();
        }
    }
}
