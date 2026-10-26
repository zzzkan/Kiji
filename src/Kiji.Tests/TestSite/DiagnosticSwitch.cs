using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite;

/// <summary>Decides what <see cref="DiagnosticPage"/> logs and counts its renders across publishes.</summary>
public sealed class DiagnosticSwitch
{
    public LogLevel? Level { get; set; }

    public int Renders { get; set; }
}
