using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/blog/{Slug}/")]
public sealed class PostPage : ComponentBase
{
    [Inject]
    public ContentCollection<Post> Posts { get; set; } = default!;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    private Post? _post;
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
        builder.AddAttribute(1, nameof(PageHead.Title), _post.Title);
        builder.AddAttribute(2, nameof(PageHead.Description), _post.Description);
        builder.CloseComponent();

        builder.OpenElement(3, "article");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, _post.Title);
        builder.CloseElement();

        if (_post.Tags.Count > 0)
        {
            builder.OpenElement(6, "ul");
            foreach (var tag in _post.Tags)
            {
                builder.OpenRegion(7);
                builder.OpenElement(7, "li");
                builder.AddContent(8, tag.Name);
                builder.CloseElement();
                builder.CloseRegion();
            }

            builder.CloseElement();
        }

        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "blog-content");
        builder.AddMarkupContent(22, _htmlContent);
        builder.CloseElement();
        builder.CloseElement();
    }
}
