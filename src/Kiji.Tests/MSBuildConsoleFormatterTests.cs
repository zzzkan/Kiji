using Kiji.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kiji.Tests;

public sealed class MSBuildConsoleFormatterTests
{
    [Theory]
    [InlineData(LogLevel.Debug, "Unchanged: index.html", "Unchanged: index.html")]
    [InlineData(LogLevel.Information, "Generated 3 pages.", "Generated 3 pages.")]
    [InlineData(LogLevel.Warning, "Alt text\nis missing.", "Site.Page : warning : Alt text is missing.")]
    [InlineData(LogLevel.Error, "The feed failed.", "Site.Page : error : The feed failed.")]
    [InlineData(LogLevel.Critical, "Stopped.", "Site.Page : error : Stopped.")]
    public void Write_UsesMSBuildFormatOnlyForWarningsAndErrors(LogLevel level, string message, string expected)
    {
        Assert.Equal(expected + Environment.NewLine, Format(level, message, exception: null));
    }

    [Fact]
    public void Write_NamesTheExceptionOnTheDiagnosticLine_ThenWritesItsDetails()
    {
        var exception = new InvalidOperationException("Missing value.");

        var lines = Format(LogLevel.Error, "Rendering failed.", exception).Split(Environment.NewLine);

        // MSBuild shows only the first line, so it carries the exception's message.
        Assert.Equal("Site.Page : error : Rendering failed. InvalidOperationException: Missing value.", lines[0]);
        Assert.StartsWith("System.InvalidOperationException: Missing value.", lines[1], StringComparison.Ordinal);
        Assert.Equal("Site.Page : warning : Caught: Missing value.",
            Format(LogLevel.Warning, "Caught: Missing value.", exception).Split(Environment.NewLine)[0]);
    }

    private static string Format(LogLevel level, string message, Exception? exception)
    {
        using var writer = new StringWriter();
        new MSBuildConsoleFormatter().Write(
            new LogEntry<string>(level, "Site.Page", default, message, exception, static (state, _) => state), null, writer);
        return writer.ToString();
    }
}
