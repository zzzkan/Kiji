using Xunit;

namespace Kiji.Tests;

public sealed class EnvironmentValueTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("TrUe", true)]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData(" true ", false)]
    public void BuildFlags_AcceptOnlyExplicitTruthyValues(string? value, bool expected)
    {
        Assert.Equal(expected, EnvironmentValue.IsTruthy(value));
    }
}
