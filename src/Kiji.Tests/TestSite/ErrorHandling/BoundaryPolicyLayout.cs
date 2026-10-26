using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class BoundaryPolicyLayout : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<ErrorBoundary>(0);
        builder.AddComponentParameter(1, nameof(ErrorBoundary.ChildContent), Body);
        builder.CloseComponent();
    }
}
