using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

[AlwaysRender]
public sealed class AlwaysRenderLayout : LayoutComponentBase
{
    [Inject] public RenderPolicyProbe Probe { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Probe.Read("layout"));
        builder.AddContent(1, Body);
    }
}
