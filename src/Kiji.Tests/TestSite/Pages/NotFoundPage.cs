using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/not-found/")]
public sealed class NotFoundPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "404");
        builder.AddAttribute(2, nameof(PageHead.Description), "ページが見つかりません。");
        builder.AddAttribute(3, nameof(PageHead.Robots), "noindex,follow");
        builder.CloseComponent();

        builder.OpenElement(4, "section");
        builder.OpenElement(5, "h1");
        builder.AddContent(6, "404");
        builder.CloseElement();
        builder.OpenElement(7, "p");
        builder.AddContent(8, "お探しのページが見つかりませんでした...");
        builder.CloseElement();
        builder.CloseElement();
    }
}
