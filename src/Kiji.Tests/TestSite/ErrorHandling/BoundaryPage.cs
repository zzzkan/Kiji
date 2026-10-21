using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

[Route("/errors/{Key}/")]
public sealed class BoundaryPage : ComponentBase
{
    [Parameter] public string Key { get; set; } = string.Empty;
    [Parameter] public string Failure { get; set; } = string.Empty;
    [Parameter] public bool UseBoundary { get; set; } = true;
    [Parameter] public bool CustomErrorContent { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!UseBoundary)
        {
            RenderChild(builder);
            return;
        }

        builder.OpenComponent<ErrorBoundary>(0);
        builder.AddComponentParameter(1, nameof(ErrorBoundary.ChildContent), (RenderFragment)RenderChild);
        if (CustomErrorContent)
        {
            builder.AddComponentParameter(2, nameof(ErrorBoundary.ErrorContent),
                (RenderFragment<Exception>)(_ => content => content.AddContent(0, $"Fallback for {Key}")));
        }
        builder.CloseComponent();
    }

    private void RenderChild(RenderTreeBuilder builder)
    {
        builder.OpenComponent<BoundaryChild>(0);
        builder.AddComponentParameter(1, nameof(BoundaryChild.Key), Key);
        builder.AddComponentParameter(2, nameof(BoundaryChild.Failure), Failure);
        builder.CloseComponent();
    }
}
