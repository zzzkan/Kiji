using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

[Route("/boundary-policy/")]
public sealed class BoundaryPolicyPage : ComponentBase
{
    [Inject] public Action RecordRender { get; set; } = default!;

    protected override void OnInitialized() => RecordRender();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<ErrorBoundary>(0);
        builder.AddComponentParameter(1, nameof(ErrorBoundary.ChildContent),
            (RenderFragment)(content => content.AddContent(0, "Healthy child")));
        builder.CloseComponent();
    }
}
