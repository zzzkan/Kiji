using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Hosting;

namespace Kiji.Tests.TestSite;

/// <summary>Writes the host environment name, as a site showing drafts only in development would branch on it.</summary>
[Route("/environment/")]
public sealed class HostEnvironmentPage : ComponentBase
{
    [Inject] public IHostEnvironment Environment { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"environment:{Environment.EnvironmentName}");
    }
}
