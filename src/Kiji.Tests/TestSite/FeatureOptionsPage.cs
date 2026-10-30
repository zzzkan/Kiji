using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Options;

namespace Kiji.Tests.TestSite;

/// <summary>Writes a bound option, as a site toggling a feature from appsettings.json would.</summary>
[Route("/feature/")]
public sealed class FeatureOptionsPage : ComponentBase
{
    [Inject] public IOptions<FeatureOptions> Options { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"enabled:{Options.Value.Enabled}");
    }
}
