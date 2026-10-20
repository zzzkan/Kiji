using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Components;

/// <summary>
/// Renders the HTML document with a <see cref="HeadOutlet"/> and a <see cref="PageView"/>.
/// </summary>
internal sealed class KijiRoot : ComponentBase
{
    [Inject]
    public SiteInfo Site { get; set; } = default!;

    [Parameter, EditorRequired]
    public Type PageType { get; set; } = default!;

    [Parameter]
    public IReadOnlyDictionary<string, object?>? PageParameters { get; set; }

    [Parameter]
    public Type? DefaultLayout { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, "<!doctype html>");
        builder.OpenElement(1, "html");
        builder.AddAttribute(2, "lang", Site.Language);

        builder.OpenElement(3, "head");
        builder.OpenComponent<HeadOutlet>(4);
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(5, "body");
        builder.OpenComponent<PageView>(6);
        builder.AddComponentParameter(7, nameof(PageView.PageType), PageType);
        builder.AddComponentParameter(8, nameof(PageView.PageParameters), PageParameters);
        builder.AddComponentParameter(9, nameof(PageView.DefaultLayout), DefaultLayout);
        builder.CloseComponent();
        builder.CloseElement();
        builder.CloseElement();
    }
}
