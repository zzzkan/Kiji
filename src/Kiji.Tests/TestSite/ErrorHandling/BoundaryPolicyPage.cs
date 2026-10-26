using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

[Route("/boundary-policy/{Kind}/")]
public sealed class BoundaryPolicyPage : ComponentBase
{
    [Inject] public BoundaryPolicyState State { get; set; } = default!;
    [Parameter] public string Kind { get; set; } = "plain";
    private bool _showBoundary = true;

    protected override async Task OnInitializedAsync()
    {
        State.Renders.AddOrUpdate(Kind, 1, static (_, count) => count + 1);
        if (Kind == "removed")
        {
            await Task.Yield();
            _showBoundary = false;
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Kind == "plain" || !_showBoundary)
        {
            builder.AddContent(0, "Healthy page");
            return;
        }

        var boundaryType = Kind switch
        {
            "custom" => typeof(CustomErrorBoundary),
            "base" or "dynamic" => typeof(CustomBaseErrorBoundary),
            _ => typeof(ErrorBoundary),
        };
        RenderFragment child = content =>
        {
            if (State.Fail)
            {
                content.OpenComponent<FailingComponent>(0);
                content.CloseComponent();
            }
            else { content.AddContent(1, "Healthy child"); }
        };
        RenderFragment<Exception> fallback = _ => content => content.AddContent(0, "Boundary fallback");

        if (Kind == "dynamic")
        {
            builder.OpenComponent<DynamicComponent>(1);
            builder.AddComponentParameter(2, nameof(DynamicComponent.Type), boundaryType);
            builder.AddComponentParameter(3, nameof(DynamicComponent.Parameters), new Dictionary<string, object>
            {
                [nameof(ErrorBoundaryBase.ChildContent)] = child,
                [nameof(ErrorBoundaryBase.ErrorContent)] = fallback,
            });
            builder.CloseComponent();
        }
        else
        {
            builder.OpenComponent(4, boundaryType);
            builder.AddComponentParameter(5, nameof(ErrorBoundaryBase.ChildContent), child);
            builder.AddComponentParameter(6, nameof(ErrorBoundaryBase.ErrorContent), fallback);
            builder.CloseComponent();
        }
    }
}
