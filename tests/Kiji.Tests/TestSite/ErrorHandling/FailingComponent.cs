using Microsoft.AspNetCore.Components;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class FailingComponent : ComponentBase
{
    protected override async Task OnInitializedAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("Private failure");
    }
}
