using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/policy/{Kind}/")]
public sealed class RenderPolicyPage : ComponentBase
{
    [Inject] public Func<string> ReadValue { get; set; } = default!;
    [Parameter] public string Kind { get; set; } = "cached";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Kind == "child")
        {
            builder.OpenComponent<DynamicComponent>(0);
            builder.AddComponentParameter(1, nameof(DynamicComponent.Type), typeof(AlwaysRenderValue));
            builder.CloseComponent();
        }
        else { builder.AddContent(2, ReadValue()); }
    }
}
