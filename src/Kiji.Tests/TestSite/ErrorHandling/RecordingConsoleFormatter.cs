using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class RecordingConsoleFormatter(RecordingLoggerProvider logs) : ConsoleFormatter("test")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        // Capture what reaches the real console provider without changing process-wide output.
        logs.Entries.Enqueue((logEntry.Category, logEntry.LogLevel, logEntry.EventId,
            logEntry.Formatter(logEntry.State, logEntry.Exception), logEntry.Exception));
    }
}
