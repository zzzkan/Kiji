using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/about/")]
public sealed class AboutPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "About");
        builder.AddAttribute(2, nameof(PageHead.Description), "zzzkan.meの紹介ページです。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "About");
        builder.CloseElement();
        builder.OpenElement(6, "p");
        builder.AddContent(7, "This is a minimal test page.");
        builder.CloseElement();
        builder.CloseElement();
    }
}
