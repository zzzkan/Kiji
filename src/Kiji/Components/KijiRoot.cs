using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;

namespace Kiji.Components;

/// <summary>
/// The built-in root document wrapping every page render: emits the HTML5 doctype,
/// <c>&lt;html lang&gt;</c> from <see cref="SiteInfo.Language"/>, a <c>&lt;head&gt;</c>
/// collecting content contributed via <see cref="Head"/>, and a <c>&lt;body&gt;</c>
/// hosting the routed page through <see cref="RouteView"/>.
/// </summary>
internal sealed class KijiRoot : ComponentBase
{
    [Inject]
    public SiteInfo Site { get; set; } = default!;

    [Parameter, EditorRequired]
    public RouteData RouteData { get; set; } = default!;

    [Parameter]
    public Type? DefaultLayout { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, "<!doctype html>");
        builder.OpenElement(1, "html");
        builder.AddAttribute(2, "lang", Site.Language);

        builder.OpenElement(3, "head");
        builder.OpenComponent<SectionOutlet>(4);
        builder.AddAttribute(5, nameof(SectionOutlet.SectionId), Head.SectionId);
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(6, "body");
        builder.OpenComponent<RouteView>(7);
        builder.AddAttribute(8, nameof(RouteView.RouteData), RouteData);
        builder.AddAttribute(9, nameof(RouteView.DefaultLayout), DefaultLayout);
        builder.CloseComponent();
        builder.CloseElement();
        builder.CloseElement();
    }
}
