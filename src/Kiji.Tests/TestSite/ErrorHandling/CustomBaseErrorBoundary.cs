using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class CustomBaseErrorBoundary : ErrorBoundaryBase
{
    protected override Task OnErrorAsync(Exception exception) => Task.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
        => builder.AddContent(0, CurrentException is null ? ChildContent : ErrorContent?.Invoke(CurrentException));
}
