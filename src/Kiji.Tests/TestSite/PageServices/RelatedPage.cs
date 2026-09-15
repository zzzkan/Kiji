using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageServices;

[Route("/related/{Key}/")]
public sealed class RelatedPage : ComponentBase
{
    [Inject] public RelatedPosts Related { get; set; } = null!;
    [Parameter] public string Key { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, $"related:{string.Join(',', Related.GetKeys(Key))}");
        builder.CloseElement();
    }
}
