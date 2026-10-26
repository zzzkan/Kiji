using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class SilentErrorBoundaryLogger : IErrorBoundaryLogger
{
    public ValueTask LogErrorAsync(Exception exception) => ValueTask.CompletedTask;
}
