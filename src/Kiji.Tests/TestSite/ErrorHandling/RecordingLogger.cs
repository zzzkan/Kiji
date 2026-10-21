using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class RecordingLogger(string category, RecordingLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        provider.Entries.Enqueue((category, logLevel, eventId, formatter(state, exception), exception));
}
