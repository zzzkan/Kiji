using Kiji.Hosting;
using Xunit;

namespace Kiji.Tests;

public sealed class DevServerStatusReporterTests
{
    [Fact]
    public void PlainOutput_ReportsAddressAndWatcherFailureWithoutAnsi()
    {
        using var output = new StringWriter();
        var reporter = new DevServerStatusReporter(output, prefix: "kiji dev", useEmoji: false, useAnsiColor: false);
        reporter.DevServerStarted(new Uri("http://127.0.0.1:8080/"), @"C:\site\contents", staticPath: null);
        reporter.WatcherError(WatchedPathSource.Content, @"C:\site\contents", new IOException("access denied"));

        var log = output.ToString();
        Assert.Contains("http://127.0.0.1:8080/", log, StringComparison.Ordinal);
        Assert.Contains(@"C:\site\contents", log, StringComparison.Ordinal);
        Assert.Contains("access denied", log, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)0x1b).ToString(), log, StringComparison.Ordinal);
    }
}
