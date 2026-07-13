using Kiji.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.SyntheticSite.Pages;

[Route("/blog/{Slug}/")]
public sealed class PostPage : ComponentBase
{
    [Inject]
    public ContentCollection<MarkdownContent<PostFrontMatter>> Posts { get; set; } = default!;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    private MarkdownContent<PostFrontMatter>? _post;
    private string _htmlContent = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _post = Posts.GetRequired(Slug);
        _htmlContent = await _post.RenderAsync();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_post is null)
        {
            return;
        }

        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), _post.FrontMatter.Title);
        builder.AddAttribute(2, nameof(PageHead.Description), _post.FrontMatter.Description);
        builder.CloseComponent();

        builder.OpenElement(3, "article");

        builder.OpenElement(4, "h1");
        builder.AddContent(5, _post.FrontMatter.Title);
        builder.CloseElement();

        if (_post.FrontMatter.Tags.Count > 0)
        {
            builder.OpenElement(6, "ul");
            foreach (var tag in _post.FrontMatter.Tags)
            {
                builder.OpenRegion(7);
                builder.OpenElement(0, "li");
                builder.AddContent(1, tag);
                builder.CloseElement();
                builder.CloseRegion();
            }

            builder.CloseElement();
        }

        builder.OpenElement(8, "div");
        builder.AddAttribute(9, "class", "post-content");
        builder.AddMarkupContent(10, _htmlContent);
        builder.CloseElement();

        builder.CloseElement();
    }
}
