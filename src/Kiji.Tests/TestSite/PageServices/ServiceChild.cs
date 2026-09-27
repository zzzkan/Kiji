using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class ServiceChild : ComponentBase
{
    [Inject] public RenderService Service { get; set; } = null!;
    [Inject] public PageInfo Page { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Service.Probe.Reads.Enqueue(("child", Service));
        Service.Probe.PageReads.Enqueue((Service, Page));
        builder.AddContent(0, Service.Key);
    }
}
