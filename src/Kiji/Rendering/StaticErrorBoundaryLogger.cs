using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

// Report boundary exceptions; component activation owns the page's reuse policy.
internal sealed partial class StaticErrorBoundaryLogger(ILogger<ErrorBoundary> logger) : IErrorBoundaryLogger
{
    public ValueTask LogErrorAsync(Exception exception)
    {
        ExceptionCaught(logger, exception.Message, exception);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(100, LogLevel.Warning, "Unhandled exception rendering component: {Message}")]
    private static partial void ExceptionCaught(ILogger logger, string message, Exception exception);
}
