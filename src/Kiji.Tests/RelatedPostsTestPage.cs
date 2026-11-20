using Kiji.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

[Route("/related/{Slug}/")]
public sealed class RelatedPostsTestPage : ComponentBase
{
    [Inject]
    public ContentCollection<MarkdownContent<FrontMatter>> Posts { get; set; } = default!;

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
        foreach (var post in Posts)
        {
            if (string.Equals(post.Path, ContentKey, StringComparison.OrdinalIgnoreCase))
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
