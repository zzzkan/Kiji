using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class OneTypePerFileAnalyzerTests
{
    [Fact]
    public async Task ReportsEverySecondTopLevelTypeInAFile()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new OneTypePerFileAnalyzer(), """
            namespace Sample;
            public sealed class First { }
            internal record Second;
            public delegate void Third();
            """);

        Assert.Equal(["KR0001", "KR0001"], diagnostics.Select(diagnostic => diagnostic.Id));
        Assert.Contains("'Second'", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Third'", diagnostics[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsNestedTypes()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new OneTypePerFileAnalyzer(), """
            namespace Sample;
            public sealed class Outer
            {
                private sealed class Inner { }
                private enum State { On }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
