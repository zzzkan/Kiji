using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

/// <summary>
/// Keeps a page that logs a warning or an error from being reused, as <see cref="AlwaysRenderAttribute"/>
/// does. A reused page logs nothing, so its message, and a publish failing on it, would disappear
/// without a fix. Once the page renders without them, the next publish can reuse it again.
/// </summary>
internal sealed class PageDiagnosticsLoggerProvider : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Warning and < LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            PageRenderContext.Current?.Dependencies?.DisableCache();
        }
    }

    public void Dispose()
    {
    }
}
