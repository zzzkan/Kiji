using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class NamespaceCycleAnalyzerTests
{
    private const string Cycle = """
        namespace Sample.Low
        {
            internal static class Reader
            {
                public static int Read() => Sample.High.Writer.Size;
            }
        }

        namespace Sample.High
        {
            internal static class Writer
            {
                public static int Size => Sample.Low.Reader.Read();
            }
        }
        """;

    [Fact]
    public async Task ReportsACycleBetweenInternalNamespaces()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new NamespaceCycleAnalyzer(), Cycle);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("KR0007", diagnostic.Id));
    }

    [Fact]
    public async Task IgnoresDependenciesThroughPublicTypes()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new NamespaceCycleAnalyzer(), """
            namespace Sample.Low
            {
                internal static class Reader
                {
                    public static int Read() => Sample.High.Api.Size;
                }
            }

            namespace Sample.High
            {
                public static class Api
                {
                    public static int Size => 1;
                }

                internal static class Writer
                {
                    public static int Size => Sample.Low.Reader.Read();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsAnUpwardDependencyAgainstTheDeclaredLayers()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new NamespaceCycleAnalyzer(), """
            namespace Sample.Low
            {
                internal static class Reader
                {
                    public static int Read() => Sample.High.Writer.Size;
                }
            }

            namespace Sample.High
            {
                internal static class Writer
                {
                    public static int Size => 1;
                }
            }
            """, new Dictionary<string, string> { ["KijiNamespaceLayers"] = "Sample.Low, Sample.High" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR0010", diagnostic.Id);
        Assert.Contains("'Reader' in Sample.Low uses 'Writer' in Sample.High", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsDownwardDependenciesAndReportsUnlistedNamespaces()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new NamespaceCycleAnalyzer(), """
            namespace Sample.Low
            {
                internal static class Reader
                {
                    public static int Read() => 1;
                }
            }

            namespace Sample.High
            {
                internal static class Writer
                {
                    public static int Size => Sample.Low.Reader.Read();
                }
            }

            namespace Sample.Elsewhere
            {
                internal static class Stray
                {
                    public static int Size => Sample.Low.Reader.Read();
                }
            }
            """, new Dictionary<string, string> { ["KijiNamespaceLayers"] = "Sample.Low, Sample.High" });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR0010", diagnostic.Id);
        Assert.StartsWith("Sample.Elsewhere holds internal types", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
