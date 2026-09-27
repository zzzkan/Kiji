using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageServices;

[Route("/services/{Key}/")]
public sealed class ServicePage : ComponentBase
{
    [Inject] public RenderService Service { get; set; } = null!;
    [Inject] public PageInfo Page { get; set; } = null!;
    [Parameter] public string Key { get; set; } = string.Empty;
    [Parameter] public bool Fail { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        Service.Key = Key;
        Service.Probe.Reads.Enqueue(("page", Service));
        Service.Probe.PageReads.Enqueue((Service, Page));
        if (Service.Probe.Release is { } release)
        {
            await release.Task;
        }

        if (Fail)
        {
            throw new InvalidOperationException("Deliberate page failure.");
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Service.Key);
        builder.OpenComponent<ServiceChild>(1);
        builder.CloseComponent();
    }
}
