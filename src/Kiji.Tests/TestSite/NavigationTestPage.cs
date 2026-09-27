using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/navigation/{Id}/")]
public sealed class NavigationTestPage : ComponentBase
{
    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Parameter] public string Id { get; set; } = "";
    [Parameter] public Action<NavigationManager> InspectNavigation { get; set; } = default!;

    protected override void OnInitialized() => InspectNavigation(Navigation);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, "Original page");
    }
}
