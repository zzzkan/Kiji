using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

[AlwaysRender]
public class VolatileComponent : ComponentBase
{
    [Inject] public RenderPolicyProbe Probe { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddContent(0, Probe.Read("child"));
}
