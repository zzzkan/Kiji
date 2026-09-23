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
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PageWithAltLayout), defaultLayout: typeof(MainLayout)));

        Assert.Contains("alt-layout", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-header", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedLayouts_RenderInsideParentLayout()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

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
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderComponentAsync<KijiRoot>(
                CreateRootParameters(typeof(PageWithCircularLayout), defaultLayout: null)));
    }

    [Fact]
    public async Task PageWithoutLayoutOrHead_RendersOnlyItsBody()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PlainPage), defaultLayout: null));

        Assert.Contains("<head></head>", html, StringComparison.Ordinal);
        Assert.Contains("plain-page", html, StringComparison.Ordinal);
        Assert.DoesNotContain("site-header", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HeadContentInLayoutAndPage_PageWins()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(PageWithTitle), defaultLayout: typeof(LayoutWithHead)));

        Assert.Contains("<title>Page Title</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Layout Title", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncPage_HeadContentPublishedAfterFirstRenderLandsInHead()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider, app.Info.BaseUrl);

        var html = await renderer.RenderComponentAsync<KijiRoot>(
            CreateRootParameters(typeof(AsyncPage), defaultLayout: null));

        Assert.Contains("<title>Async Title</title>", html, StringComparison.Ordinal);
        Assert.Contains("async-page-body", html, StringComparison.Ordinal);
    }

    private static StaticSite CreateApp()
    {
        var siteInfo = TestArticleContents.CreateSiteInfo();

        var app = StaticSite.Create([]);
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

    private sealed class LayoutWithHead : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<HeadContent>(0);
            builder.AddAttribute(1, nameof(HeadContent.ChildContent), (RenderFragment)(static headBuilder =>
            {
                headBuilder.OpenElement(0, "title");
                headBuilder.AddContent(1, "Layout Title");
                headBuilder.CloseElement();
            }));
            builder.CloseComponent();

            builder.AddContent(2, Body);
        }
    }

    private sealed class PageWithTitle : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<HeadContent>(0);
            builder.AddAttribute(1, nameof(HeadContent.ChildContent), (RenderFragment)(static headBuilder =>
            {
                headBuilder.OpenElement(0, "title");
                headBuilder.AddContent(1, "Page Title");
                headBuilder.CloseElement();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class AsyncPage : ComponentBase
    {
        private bool _initialized;

        protected override async Task OnInitializedAsync()
        {
            await Task.Yield();
            _initialized = true;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (!_initialized)
            {
                return;
            }

            builder.OpenComponent<HeadContent>(0);
            builder.AddAttribute(1, nameof(HeadContent.ChildContent), (RenderFragment)(static headBuilder =>
            {
                headBuilder.OpenElement(0, "title");
                headBuilder.AddContent(1, "Async Title");
                headBuilder.CloseElement();
            }));
            builder.CloseComponent();

            builder.OpenElement(2, "p");
            builder.AddAttribute(3, "class", "async-page-body");
            builder.AddContent(4, "async");
            builder.CloseElement();
        }
    }
}
