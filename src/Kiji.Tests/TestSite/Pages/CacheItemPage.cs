using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/cache/{Id}/")]
public sealed class CacheItemPage : ComponentBase
{
    [Parameter] public string Id { get; set; } = string.Empty;
    [Inject] public ContentCollection<CacheItem> Items { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var item = Items[Id];
        item.Rendered();
        builder.AddContent(0, item.Text);
    }
}
