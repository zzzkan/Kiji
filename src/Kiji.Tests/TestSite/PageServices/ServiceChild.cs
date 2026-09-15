using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class ServiceChild : ComponentBase
{
    [Inject] public RenderService Service { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Service.Probe.Reads.Enqueue(("child", Service));
        builder.AddContent(0, Service.Key);
    }
}
