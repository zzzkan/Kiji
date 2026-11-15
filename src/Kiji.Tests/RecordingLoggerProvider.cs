using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kiji.Tests;

/// <summary>Records every message that the site's logging filters let through.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _entries = new();

    public IReadOnlyCollection<(string Category, LogLevel Level, string Message)> Entries => _entries;

    public ConcurrentQueue<Exception> Exceptions { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries, Exceptions);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<(string Category, LogLevel Level, string Message)> entries, ConcurrentQueue<Exception> exceptions) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue((category, logLevel, formatter(state, exception)));
            if (exception is not null) { exceptions.Enqueue(exception); }
        }
    }
}
