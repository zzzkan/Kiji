using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="KijiCommandLine"/> argument parsing.
/// </summary>
public sealed class KijiCommandLineTests
{
    [Fact]
    public void Parse_NoArguments_DefaultsToBuild()
    {
        var command = KijiCommandLine.Parse([]);

        Assert.Equal(KijiCommandKind.Build, command.Kind);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("BUILD")]
    [InlineData("Build")]
    public void Parse_BuildCommand_IsCaseInsensitive(string arg)
    {
        var command = KijiCommandLine.Parse([arg]);

        Assert.Equal(KijiCommandKind.Build, command.Kind);
    }

    [Fact]
    public void Parse_DevWithoutPort_UsesDefaultPort()
    {
        var command = KijiCommandLine.Parse(["dev"]);

        Assert.Equal(KijiCommandKind.Dev, command.Kind);
        Assert.Equal(8080, command.Port);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("preview")]
    public void Parse_WithPort_ParsesPort(string arg)
    {
        var expectedKind = arg == "dev" ? KijiCommandKind.Dev : KijiCommandKind.Preview;

        var command = KijiCommandLine.Parse([arg, "--port", "5000"]);

        Assert.Equal(expectedKind, command.Kind);
        Assert.Equal(5000, command.Port);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void Parse_InvalidPort_ThrowsArgumentException(string port)
    {
        var exception = Assert.Throws<ArgumentException>(() => KijiCommandLine.Parse(["dev", "--port", port]));

        Assert.Contains(port, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("serve")]
    [InlineData("clean")]
    public void Parse_UnknownCommand_ReportsRawCommand(string arg)
    {
        var command = KijiCommandLine.Parse([arg]);

        Assert.Equal(KijiCommandKind.Unknown, command.Kind);
        Assert.Equal(arg, command.RawCommand);
    }

    [Fact]
    public void Parse_PortValueMissing_UsesDefaultPort()
    {
        var command = KijiCommandLine.Parse(["dev", "--port"]);

        Assert.Equal(KijiCommandKind.Dev, command.Kind);
        Assert.Equal(KijiCommandLine.DefaultPort, command.Port);
    }
}
