using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

[AlwaysRender]
[Route("/always/{Id}/")]
public sealed class AlwaysRenderPage : ComponentBase
{
    [Parameter] public string Id { get; set; } = string.Empty;
    [Inject] public RenderPolicyProbe Probe { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddContent(0, Probe.Read("always-" + Id));
}
