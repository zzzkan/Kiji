using Kiji.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.SyntheticSite.Pages;

[Route("/blog/")]
public sealed class BlogIndexPage : ComponentBase
{
    [Inject]
    public ContentCollection<MarkdownContent<PostFrontMatter>> Posts { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "Blog");
        builder.AddAttribute(2, nameof(PageHead.Description), "All synthetic posts.");
        builder.CloseComponent();

        builder.OpenElement(3, "h1");
        builder.AddContent(4, "Blog");
        builder.CloseElement();

        builder.OpenElement(5, "ul");
        foreach (var post in Posts)
        {
            var slug = PostSlug.From(post.FileInfo);

            builder.OpenRegion(6);
            builder.OpenElement(0, "li");
            builder.OpenElement(1, "a");
            builder.AddAttribute(2, "href", $"/blog/{slug}/");
            builder.AddContent(3, post.FrontMatter.Title);
            builder.CloseElement();
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}
