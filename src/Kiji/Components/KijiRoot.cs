using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Components;

/// <summary>
/// The built-in root document wrapping every page render: emits the HTML5 doctype,
/// <c>&lt;html lang&gt;</c> from <see cref="SiteInfo.Language"/>, a <c>&lt;head&gt;</c>
/// collecting content contributed via <see cref="HeadContent"/> through
/// <see cref="HeadOutlet"/>, and a <c>&lt;body&gt;</c> hosting the page through
/// <see cref="PageView"/>.
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
