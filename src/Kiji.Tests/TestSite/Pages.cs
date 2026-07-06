using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/")]
public sealed class HomePage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), "Home");
        builder.AddAttribute(2, nameof(Head.Description), "zzzkan.meのホームページです。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "Home");
        builder.CloseElement();
        builder.OpenElement(6, "p");
        builder.AddContent(7, "ようこそ！そのうち充実させます。");
        builder.CloseElement();
        builder.CloseElement();
    }
}

[Route("/about/")]
public sealed class AboutPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), "About");
        builder.AddAttribute(2, nameof(Head.Description), "zzzkan.meの紹介ページです。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "About");
        builder.CloseElement();
        builder.OpenElement(6, "p");
        builder.AddContent(7, "This is a minimal test page.");
        builder.CloseElement();
        builder.CloseElement();
    }
}

[Route("/privacy-policy/")]
public sealed class PrivacyPolicyPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), "Privacy Policy");
        builder.AddAttribute(2, nameof(Head.Description), "プライバシーポリシーです。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, "Privacy Policy");
        builder.CloseElement();
        builder.CloseElement();
    }
}

[Route("/not-found/")]
public sealed class NotFoundPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), "404");
        builder.AddAttribute(2, nameof(Head.Description), "ページが見つかりません。");
        builder.AddAttribute(3, nameof(Head.Robots), "noindex,follow");
        builder.CloseComponent();

        builder.OpenElement(4, "section");
        builder.OpenElement(5, "h1");
        builder.AddContent(6, "404");
        builder.CloseElement();
        builder.OpenElement(7, "p");
        builder.AddContent(8, "お探しのページが見つかりませんでした...");
        builder.CloseElement();
        builder.CloseElement();
    }
}

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
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), "Blog");
        builder.AddAttribute(2, nameof(Head.Description), "ブログ記事一覧です。");
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

        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), _post.Title);
        builder.AddAttribute(2, nameof(Head.Description), _post.Description);
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

[Route("/tags/{TagSlug}/")]
public sealed class TagsPage : ComponentBase
{
    [Inject]
    public ContentCollection<Post> Posts { get; set; } = default!;

    [Parameter]
    public string TagSlug { get; set; } = string.Empty;

    private List<Post> _posts = [];
    private string _tagName = string.Empty;

    protected override void OnParametersSet()
    {
        var tag = Posts.Items
            .SelectMany(static post => post.Tags)
            .Where(tag => string.Equals(tag.UrlSlug, TagSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        _tagName = tag?.Name
            ?? throw new KeyNotFoundException($"Tag '{TagSlug}' was not found.");
        _posts =
        [
            .. Posts.Items
                .Where(post => post.Tags.Any(tagItem => string.Equals(tagItem.UrlSlug, TagSlug, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(static post => post.CreatedAt),
        ];
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<Head>(0);
        builder.AddAttribute(1, nameof(Head.Title), $"Tag: {_tagName}");
        builder.AddAttribute(2, nameof(Head.Description), $"Tagとして'{_tagName}'が付けられた記事一覧です。");
        builder.CloseComponent();

        builder.OpenElement(3, "section");
        builder.OpenElement(4, "h1");
        builder.AddContent(5, $"Tag: {_tagName}");
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