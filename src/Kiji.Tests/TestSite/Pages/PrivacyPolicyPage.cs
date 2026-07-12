using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/privacy-policy/")]
public sealed class PrivacyPolicyPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "Privacy Policy");
        builder.AddAttribute(2, nameof(PageHead.Description), "プライバシーポリシーです。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "Privacy Policy");
        builder.CloseElement();
        builder.CloseElement();
    }
}
