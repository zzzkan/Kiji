using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/environment/")]
public sealed class HostEnvironmentPage : ComponentBase
{
    [Inject] public IHostEnvironment Environment { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"environment:{Environment.EnvironmentName}");
    }
}
