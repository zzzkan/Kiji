using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

[Route("/policy/{Id}/")]
public sealed class RenderPolicyPage : ComponentBase
{
    [Parameter] public string Id { get; set; } = string.Empty;
    [Parameter] public bool IncludeVolatile { get; set; }
    [Inject] public RenderPolicyProbe Probe { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Probe.Read(Id));
        if (IncludeVolatile)
        {
            builder.OpenComponent<RenderPolicyWrapper>(1);
            builder.CloseComponent();
        }
        builder.OpenElement(2, "a");
        builder.AddAttribute(3, "href", "/always/one/");
        builder.AddContent(4, "A link alone does not disable reuse.");
        builder.CloseElement();
    }
}
