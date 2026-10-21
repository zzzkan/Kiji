using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class ScopedBoundaryLogger(PageInfo page) : IErrorBoundaryLogger, IAsyncDisposable
{
    public PageInfo Page { get; } = page;
    public List<Exception> Exceptions { get; } = [];
    public bool Disposed { get; private set; }

    public ValueTask LogErrorAsync(Exception exception)
    {
        Exceptions.Add(exception);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
