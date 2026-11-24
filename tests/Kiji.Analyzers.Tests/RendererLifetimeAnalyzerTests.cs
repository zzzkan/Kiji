using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class RendererLifetimeAnalyzerTests
{
    private const string Framework = """
        namespace Microsoft.AspNetCore.Components.RenderTree
        {
            public abstract class Renderer { }
        }

        namespace Microsoft.AspNetCore.Components.Web
        {
            public sealed class HtmlRenderer { }
        }

        namespace Sample
        {
            internal sealed class PageRenderer : Microsoft.AspNetCore.Components.RenderTree.Renderer { }
        }
        """;

    [Fact]
    public async Task ReportsRenderersKeptInFieldsPropertiesAndPools()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new RendererLifetimeAnalyzer(), Framework + """

            namespace Sample
            {
                internal sealed class Holder
                {
                    private PageRenderer? _renderer;
                    private readonly System.Collections.Generic.Stack<PageRenderer> _pool = new();
                    public Microsoft.AspNetCore.Components.Web.HtmlRenderer? Html { get; set; }
                    private static PageRenderer[] s_cache = [];
                }
            }
            """);

        Assert.Equal(["_renderer", "_pool", "Html", "s_cache"],
            diagnostics.Select(diagnostic => diagnostic.GetMessage().Split('\'')[1]));
        Assert.All(diagnostics, diagnostic => Assert.Equal("KR0011", diagnostic.Id));
    }

    [Fact]
    public async Task AllowsARendererLocalToOneRender()
    {
        var diagnostics = await AnalyzerRun.DiagnosticsAsync(new RendererLifetimeAnalyzer(), Framework + """

            namespace Sample
            {
                internal static class Rendering
                {
                    public static object Render()
                    {
                        var renderer = new PageRenderer();
                        return renderer;
                    }
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
