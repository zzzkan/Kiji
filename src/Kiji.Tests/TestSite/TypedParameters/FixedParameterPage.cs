using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Tests.TestSite.TypedParameters;

[Route("/fixed-parameters/")]
public sealed class FixedParameterPage(IServiceProvider services) : ComponentBase
{
    [Parameter] public string Title { get; set; } = "Default title";
    [Parameter] public int Count { get; set; } = 7;
    [Parameter, EditorRequired] public string? Description { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        services.GetService<FixedParameterLog>()?.Record();
        builder.AddContent(0, FormattableString.Invariant($"{Title}|{Count}|{Description}"));
    }
}
