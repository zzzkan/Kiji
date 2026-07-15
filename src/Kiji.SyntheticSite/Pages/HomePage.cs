using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.SyntheticSite.Pages;

[Route("/")]
public sealed class HomePage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "Home");
        builder.AddAttribute(2, nameof(PageHead.Description), "Synthetic benchmark site home page.");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "Kiji synthetic benchmark site");
        builder.CloseElement();
        builder.OpenElement(6, "p");
        builder.AddContent(7, "Deterministic content used to measure build performance.");
        builder.CloseElement();
        builder.CloseElement();
    }
}
