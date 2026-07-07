using Xunit;

namespace Kiji.Tests;

/// <summary>
/// Unit tests for <see cref="KijiCommandLine"/> argument parsing.
/// </summary>
public sealed class KijiCommandLineTests
{
    [Fact]
    public void Parse_NoArguments_DefaultsToBuildWithClean()
    {
        var command = KijiCommandLine.Parse([]);

        Assert.Equal(KijiCommandKind.Build, command.Kind);
        Assert.Null(command.Output);
        Assert.True(command.Clean);
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
    public void Parse_BuildWithOutputAndNoClean_ParsesOptions()
    {
        var command = KijiCommandLine.Parse(["build", "--output", "custom/dist", "--no-clean"]);

        Assert.Equal(KijiCommandKind.Build, command.Kind);
        Assert.Equal("custom/dist", command.Output);
        Assert.False(command.Clean);
    }

    [Fact]
    public void Parse_CleanCommand_ReturnsClean()
    {
        var command = KijiCommandLine.Parse(["clean"]);

        Assert.Equal(KijiCommandKind.Clean, command.Kind);
    }

    [Fact]
    public void Parse_ServeWithoutPort_UsesDefaultPort()
    {
        var command = KijiCommandLine.Parse(["serve"]);

        Assert.Equal(KijiCommandKind.Serve, command.Kind);
        Assert.Equal(8080, command.Port);
    }

    [Theory]
    [InlineData("serve")]
    [InlineData("preview")]
    public void Parse_WithPort_ParsesPort(string arg)
    {
        var expectedKind = arg == "serve" ? KijiCommandKind.Serve : KijiCommandKind.Preview;

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
        var exception = Assert.Throws<ArgumentException>(() => KijiCommandLine.Parse(["serve", "--port", port]));

        Assert.Contains(port, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownCommand_ReportsRawCommand()
    {
        var command = KijiCommandLine.Parse(["deploy"]);

        Assert.Equal(KijiCommandKind.Unknown, command.Kind);
        Assert.Equal("deploy", command.RawCommand);
    }

    [Fact]
    public void Parse_OptionValueMissing_IsIgnored()
    {
        var command = KijiCommandLine.Parse(["build", "--output"]);

        Assert.Equal(KijiCommandKind.Build, command.Kind);
        Assert.Null(command.Output);
    }
}
