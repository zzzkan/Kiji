using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.SyntheticSite;

/// <summary>
/// Contributes standard head content (charset, title, description, canonical) for a page.
/// </summary>
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

        builder.OpenElement(2, "title");
        builder.AddContent(3, $"{Title} - {Site.Name}");
        builder.CloseElement();

        builder.OpenElement(4, "meta");
        builder.AddAttribute(5, "name", "description");
        builder.AddAttribute(6, "content", Description);
        builder.CloseElement();

        builder.OpenElement(7, "link");
        builder.AddAttribute(8, "rel", "canonical");
        builder.AddAttribute(9, "href", NavigationManager.Uri);
        builder.CloseElement();

        builder.OpenElement(10, "link");
        builder.AddAttribute(11, "rel", "stylesheet");
        builder.AddAttribute(12, "href", "/css/app.css");
        builder.CloseElement();
    }
}
