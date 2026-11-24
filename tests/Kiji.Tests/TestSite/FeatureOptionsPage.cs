using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Options;

namespace Kiji.Tests.TestSite;

[Route("/feature/")]
public sealed class FeatureOptionsPage : ComponentBase
{
    [Inject] public IOptions<FeatureOptions> Options { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"enabled:{Options.Value.Enabled}");
    }
}
