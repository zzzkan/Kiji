using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Kiji.Hosting;

/// <summary>
/// Formats console output for <c>dotnet publish</c>, which runs the site through MSBuild's Exec task.
/// Warnings and errors use MSBuild's canonical format, so MSBuild reports them as warnings and errors:
/// the terminal logger shows nothing else from the site. Other messages are written as plain text.
/// </summary>
internal sealed class MSBuildConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    internal const string FormatterName = "kiji-msbuild";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        var exception = logEntry.Exception;
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var kind = logEntry.LogLevel switch
        {
            LogLevel.Warning => "warning",
            LogLevel.Error or LogLevel.Critical => "error",
            _ => null,
        };
        textWriter.WriteLine(kind is null ? message : FormatDiagnostic(logEntry.Category, kind, message, exception));
        if (exception is not null)
        {
            // Details for CI logs and detailed verbosity; the terminal logger hides them.
            textWriter.WriteLine(exception.ToString());
        }
    }

    /// <summary>Formats one line that MSBuild recognizes as a warning or an error.</summary>
    internal static string FormatDiagnostic(string origin, string kind, string? message, Exception? exception)
    {
        var text = message ?? string.Empty;
        // MSBuild reads only this line, so it names the exception unless the message already does.
        if (exception is not null && !text.Contains(exception.Message, StringComparison.Ordinal))
        {
            text = string.IsNullOrEmpty(text)
                ? $"{exception.GetType().Name}: {exception.Message}"
                : $"{text} {exception.GetType().Name}: {exception.Message}";
        }
        return $"{origin} : {kind} : {text.ReplaceLineEndings(" ")}";
    }
}
