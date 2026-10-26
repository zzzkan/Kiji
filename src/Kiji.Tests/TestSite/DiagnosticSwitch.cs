using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite;

/// <summary>Controls <see cref="DiagnosticPage"/> and counts its renders across publishes.</summary>
public sealed class DiagnosticSwitch
{
    public LogLevel? Level { get; set; }

    public bool Fail { get; set; }

    public int Renders { get; set; }
}
