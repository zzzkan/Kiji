using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/page-info/{Key}/")]
public sealed class PageInfoTestPage : ComponentBase
{
    [Inject] public PageInfo Page { get; set; } = default!;
    [Inject] public SiteInfo Site { get; set; } = default!;
    [Parameter] public string Key { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Page.Url.AbsoluteUri);
        builder.AddContent(1, "|");
        builder.AddContent(2, Site.ResolveUrl("about/").AbsolutePath);
        builder.AddContent(3, "|");
        builder.AddContent(4, new Uri(Page.Url, "image.png").AbsolutePath);
        builder.AddContent(5, "|");
        builder.AddContent(6, Site.BaseUrl.MakeRelativeUri(Page.Url).OriginalString);
    }
}
