using Microsoft.AspNetCore.Components;

namespace Kiji.Tests.TestSite;

public sealed class FailingRenderComponent : ComponentBase
{
    [Parameter] public InvalidOperationException Error { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Task.Yield();
        throw Error;
    }
}
