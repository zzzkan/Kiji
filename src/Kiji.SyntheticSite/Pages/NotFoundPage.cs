using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.SyntheticSite.Pages;

[Route("/not-found/")]
public sealed class NotFoundPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "Not Found");
        builder.AddAttribute(2, nameof(PageHead.Description), "Page not found.");
        builder.CloseComponent();

        builder.OpenElement(3, "h1");
        builder.AddContent(4, "404 - Page not found");
        builder.CloseElement();
    }
}
