using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

    public void Dispose() { }
}
