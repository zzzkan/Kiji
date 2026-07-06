using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;

namespace Kiji.Tests.TestSite;

public sealed class Root : ComponentBase
{
    [Parameter, EditorRequired]
    public RouteData RouteData { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, "<!doctype html>");
        builder.OpenElement(1, "html");
        builder.AddAttribute(2, "lang", "ja");

        builder.OpenElement(3, "head");
        builder.OpenElement(4, "meta");
        builder.AddAttribute(5, "charset", "utf-8");
        builder.CloseElement();
        builder.OpenElement(6, "meta");
        builder.AddAttribute(7, "name", "viewport");
        builder.AddAttribute(8, "content", "width=device-width, initial-scale=1.0");
        builder.CloseElement();
        builder.OpenComponent<SectionOutlet>(9);
        builder.AddAttribute(10, nameof(SectionOutlet.SectionName), "Head");
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(11, "body");
        builder.OpenComponent<RouteView>(12);
        builder.AddAttribute(13, nameof(RouteView.RouteData), RouteData);
        builder.AddAttribute(14, nameof(RouteView.DefaultLayout), typeof(MainLayout));
        builder.CloseComponent();
        builder.CloseElement();
        builder.CloseElement();
    }
}

public sealed class MainLayout : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "site");

        builder.OpenElement(2, "header");
        builder.AddAttribute(3, "class", "site-header");
        builder.OpenElement(4, "a");
        builder.AddAttribute(5, "href", "/");
        builder.AddAttribute(6, "class", "site-title");
        builder.AddContent(7, "zzzkan.me");
        builder.CloseElement();
        builder.OpenElement(8, "nav");
        builder.AddAttribute(9, "aria-label", "Main navigation");
        builder.OpenElement(10, "a");
        builder.AddAttribute(11, "href", "/about/");
        builder.AddContent(12, "About");
        builder.CloseElement();
        builder.OpenElement(13, "a");
        builder.AddAttribute(14, "href", "/blog/");
        builder.AddContent(15, "Blog");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(16, "main");
        builder.AddAttribute(17, "id", "main-content");
        builder.AddContent(18, Body);
        builder.CloseElement();

        builder.OpenElement(19, "footer");
        builder.OpenElement(20, "a");
        builder.AddAttribute(21, "href", "/privacy-policy/");
        builder.AddContent(22, "Privacy");
        builder.CloseElement();
        builder.CloseElement();

        builder.CloseElement();
    }
}

public sealed class Head : ComponentBase
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public SiteInfo Site { get; set; } = default!;

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Description { get; set; } = string.Empty;

    [Parameter]
    public string? Robots { get; set; }

    private string PageTitle => string.IsNullOrWhiteSpace(Title) ? Site.Name : $"{Title} - {Site.Name}";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<SectionContent>(0);
        builder.AddAttribute(1, nameof(SectionContent.SectionName), "Head");
        builder.AddAttribute(2, nameof(SectionContent.ChildContent), (RenderFragment)BuildHeadContent);
        builder.CloseComponent();
    }

    private void BuildHeadContent(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "title");
        builder.AddContent(1, PageTitle);
        builder.CloseElement();

        builder.OpenElement(2, "meta");
        builder.AddAttribute(3, "name", "description");
        builder.AddAttribute(4, "content", Description);
        builder.CloseElement();

        if (Robots is not null)
        {
            builder.OpenElement(5, "meta");
            builder.AddAttribute(6, "name", "robots");
            builder.AddAttribute(7, "content", Robots);
            builder.CloseElement();
        }

        builder.OpenElement(8, "link");
        builder.AddAttribute(9, "rel", "canonical");
        builder.AddAttribute(10, "href", NavigationManager.Uri);
        builder.CloseElement();

        builder.OpenElement(11, "link");
        builder.AddAttribute(12, "rel", "alternate");
        builder.AddAttribute(13, "type", "application/rss+xml");
        builder.AddAttribute(14, "title", Site.Name);
        builder.AddAttribute(15, "href", new Uri(Site.BaseUrl, "feed.xml").AbsoluteUri);
        builder.CloseElement();

        builder.OpenElement(16, "link");
        builder.AddAttribute(17, "rel", "stylesheet");
        builder.AddAttribute(18, "href", "/css/app.css");
        builder.CloseElement();

        builder.OpenElement(19, "link");
        builder.AddAttribute(20, "rel", "icon");
        builder.AddAttribute(21, "href", "/icon.svg");
        builder.AddAttribute(22, "type", "image/svg+xml");
        builder.CloseElement();
    }
}

public sealed class PostListComponent : ComponentBase
{
    [Parameter, EditorRequired]
    public IReadOnlyList<Post> Posts { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");

        foreach (var post in Posts)
        {
            builder.OpenRegion(1);
            builder.OpenElement(1, "li");
            builder.OpenElement(2, "a");
            builder.AddAttribute(3, "href", $"/blog/{post.SlugUrlEncoded}/");
            builder.AddContent(4, post.Title);
            builder.CloseElement();
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}