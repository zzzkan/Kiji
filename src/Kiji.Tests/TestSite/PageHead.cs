using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

public sealed class PageHead : ComponentBase
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
        builder.OpenComponent<Kiji.Components.HeadContent>(0);
        builder.AddAttribute(1, nameof(Kiji.Components.HeadContent.ChildContent), (RenderFragment)BuildHeadContent);
        builder.CloseComponent();
    }

    private void BuildHeadContent(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "meta");
        builder.AddAttribute(1, "charset", "utf-8");
        builder.CloseElement();

        builder.OpenElement(2, "meta");
        builder.AddAttribute(3, "name", "viewport");
        builder.AddAttribute(4, "content", "width=device-width, initial-scale=1.0");
        builder.CloseElement();

        builder.OpenElement(5, "title");
        builder.AddContent(6, PageTitle);
        builder.CloseElement();

        builder.OpenElement(7, "meta");
        builder.AddAttribute(8, "name", "description");
        builder.AddAttribute(9, "content", Description);
        builder.CloseElement();

        if (Robots is not null)
        {
            builder.OpenElement(10, "meta");
            builder.AddAttribute(11, "name", "robots");
            builder.AddAttribute(12, "content", Robots);
            builder.CloseElement();
        }

        builder.OpenElement(13, "link");
        builder.AddAttribute(14, "rel", "canonical");
        builder.AddAttribute(15, "href", NavigationManager.Uri);
        builder.CloseElement();

        builder.OpenElement(16, "link");
        builder.AddAttribute(17, "rel", "alternate");
        builder.AddAttribute(18, "type", "application/rss+xml");
        builder.AddAttribute(19, "title", Site.Name);
        builder.AddAttribute(20, "href", new Uri(Site.BaseUrl, "feed.xml").AbsoluteUri);
        builder.CloseElement();

        builder.OpenElement(21, "link");
        builder.AddAttribute(22, "rel", "stylesheet");
        builder.AddAttribute(23, "href", Site.Path("css/app.css"));
        builder.CloseElement();

        builder.OpenElement(24, "link");
        builder.AddAttribute(25, "rel", "icon");
        builder.AddAttribute(26, "href", Site.Path("icon.svg"));
        builder.AddAttribute(27, "type", "image/svg+xml");
        builder.CloseElement();
    }
}
