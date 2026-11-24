using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class ErrorBoundaryComponent : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<ErrorBoundary>(0);
        builder.AddComponentParameter(1, nameof(ErrorBoundary.ChildContent), (RenderFragment)(content =>
        {
            content.OpenComponent<FailingComponent>(0);
            content.CloseComponent();
        }));
        builder.AddComponentParameter(2, nameof(ErrorBoundary.ErrorContent),
            (RenderFragment<Exception>)(_ => content => content.AddContent(0, "Boundary fallback")));
        builder.CloseComponent();
    }
}
