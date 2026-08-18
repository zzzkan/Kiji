using Kiji.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

/// <summary>
/// A detail page that also computes a "related posts" list by enumerating the whole
/// dictionary — the recommended shape for derived data. Enumerating is what records the
/// content-set dependency, so this page must re-render when any post changes, not only
/// when its own does.
/// </summary>
[Route("/related/{Slug}/")]
public sealed class RelatedPostsTestPage : ComponentBase
{
    [Inject]
    public ContentDictionary<MarkdownContent<FrontMatter>> Posts { get; set; } = default!;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Parameter]
    public string ContentKey { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "h1");
        builder.AddContent(1, Posts[ContentKey].FrontMatter.Title);
        builder.CloseElement();

        builder.OpenElement(2, "ul");
        foreach (var (key, post) in Posts)
        {
            if (string.Equals(key, ContentKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.OpenRegion(3);
            builder.OpenElement(0, "li");
            builder.AddContent(1, post.FrontMatter.Title);
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}
