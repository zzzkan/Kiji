using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Benchmarks;

/// <summary>
/// A representative page component: head contribution plus a body with headings,
/// a list, and an embedded markup block (as a rendered markdown article would have).
/// </summary>
[Route("/bench/")]
public sealed class BenchPage : ComponentBase
{
    private static readonly string ArticleHtml = string.Join(
        "\n",
        Enumerable.Range(0, 30).Select(static i =>
            $"<p>Paragraph {i} of the pre-rendered article body, standing in for markdown output.</p>"));

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Kiji.Components.StaticHeadContent>(0);
        builder.AddAttribute(1, nameof(Kiji.Components.StaticHeadContent.ChildContent), (RenderFragment)BuildHeadContent);
        builder.CloseComponent();

        builder.OpenElement(2, "article");
        builder.OpenElement(3, "h1");
        builder.AddContent(4, "Benchmark page");
        builder.CloseElement();

        builder.OpenElement(5, "ul");
        for (var i = 0; i < 10; i++)
        {
            builder.OpenRegion(6);
            builder.OpenElement(0, "li");
            builder.AddContent(1, $"Item {i}");
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();

        builder.OpenElement(7, "div");
        builder.AddMarkupContent(8, ArticleHtml);
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void BuildHeadContent(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "meta");
        builder.AddAttribute(1, "charset", "utf-8");
        builder.CloseElement();

        builder.OpenElement(2, "title");
        builder.AddContent(3, "Benchmark page");
        builder.CloseElement();

        builder.OpenElement(4, "meta");
        builder.AddAttribute(5, "name", "description");
        builder.AddAttribute(6, "content", "A representative page for renderer benchmarks.");
        builder.CloseElement();
    }
}
