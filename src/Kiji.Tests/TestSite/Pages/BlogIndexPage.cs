using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/blog/")]
public sealed class BlogIndexPage : ComponentBase
{
    [Inject]
    public ContentCollection<Post> AllPosts { get; set; } = default!;

    private List<Post> _posts = [];

    protected override void OnInitialized()
    {
        _posts = [.. AllPosts.Items.OrderByDescending(static post => post.CreatedAt)];
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), "Blog");
        builder.AddAttribute(2, nameof(PageHead.Description), "ブログ記事一覧です。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "Blog");
        builder.CloseElement();

        if (_posts.Count > 0)
        {
            builder.OpenComponent<PostListComponent>(6);
            builder.AddAttribute(7, nameof(PostListComponent.Posts), _posts);
            builder.CloseComponent();
        }
        else
        {
            builder.OpenElement(8, "p");
            builder.AddContent(9, "記事がありません。");
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
