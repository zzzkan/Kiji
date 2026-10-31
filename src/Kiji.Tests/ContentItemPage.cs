using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

/// <summary>Renders a collection item of any model type received as a page parameter.</summary>
[Route("/content-items/{Slug}/")]
public sealed class ContentItemPage : ComponentBase
{
    [Inject] public ContentRenderLog Log { get; set; } = default!;
    [Parameter] public string Slug { get; set; } = "";
    [Parameter] public object? Item { get; set; }

    protected override void OnParametersSet() => Log.Record(Slug);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Item is FrontMatter frontMatter ? frontMatter.Title : Item);
    }
}
