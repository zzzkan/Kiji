using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/navigation/{Id}/")]
public sealed class UnsupportedNavigationPage : ComponentBase
{
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Parameter] public string Id { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, "Original page");
    }
}
