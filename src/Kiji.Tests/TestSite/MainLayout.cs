using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

public sealed class MainLayout : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "site");

        builder.OpenElement(2, "header");
        builder.AddAttribute(3, "class", "site-header");
        builder.OpenElement(4, "a");
        builder.AddAttribute(5, "href", "/");
        builder.AddAttribute(6, "class", "site-title");
        builder.AddContent(7, "zzzkan.me");
        builder.CloseElement();
        builder.OpenElement(8, "nav");
        builder.AddAttribute(9, "aria-label", "Main navigation");
        builder.OpenElement(10, "a");
        builder.AddAttribute(11, "href", "/about/");
        builder.AddContent(12, "About");
        builder.CloseElement();
        builder.OpenElement(13, "a");
        builder.AddAttribute(14, "href", "/blog/");
        builder.AddContent(15, "Blog");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(16, "main");
        builder.AddAttribute(17, "id", "main-content");
        builder.AddContent(18, Body);
        builder.CloseElement();

        builder.OpenElement(19, "footer");
        builder.OpenElement(20, "a");
        builder.AddAttribute(21, "href", "/privacy-policy/");
        builder.AddContent(22, "Privacy");
        builder.CloseElement();
        builder.CloseElement();

        builder.CloseElement();
    }
}
