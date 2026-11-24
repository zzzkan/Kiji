using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class CommentAnalyzerTests
{
    private static readonly Dictionary<string, string> PublicApi = new() { ["KijiPublicApi"] = "true" };

    [Fact]
    public async Task ReportsLineBlockAndTrailingComments()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new CommentAnalyzer(), """
            namespace Sample;
            // line
            internal static class Values
            {
                /* block */
                public const int One = 1; // trailing
            }
            """, PublicApi);

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("KR0004", diagnostic.Id));
    }

    [Fact]
    public async Task IgnoresCommentLikeTextInStringsAndDirectives()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new CommentAnalyzer(), """
            #pragma warning disable CS0414
            namespace Sample;
            internal static class Values
            {
                public const string Url = "https://example.com/* not a comment */";
                public const string Slashes = "// not a comment";
            }
            """, PublicApi);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AllowsDocumentationOnlyOnThePublicApiOfALibrary()
    {
        const string source = """
            namespace Sample;
            /// <summary>Visible.</summary>
            public sealed class Visible
            {
                /// <summary>Visible member.</summary>
                public int Member { get; }

                /// <summary>Hidden member.</summary>
                internal int Hidden { get; }
            }

            /// <summary>Hidden type.</summary>
            internal sealed class Hidden { }
            """;

        var library = await AnalyzerRun.DiagnosticsAsync(new CommentAnalyzer(), source, PublicApi);
        var other = await AnalyzerRun.DiagnosticsAsync(new CommentAnalyzer(), source);

        Assert.Equal(2, library.Count);
        Assert.All(library, diagnostic => Assert.Contains("public API", diagnostic.GetMessage(), StringComparison.Ordinal));
        Assert.Equal(4, other.Count);
    }
}
