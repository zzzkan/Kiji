using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class CustomErrorBoundary : ErrorBoundary
{
    protected override Task OnErrorAsync(Exception exception) => Task.CompletedTask;
}
