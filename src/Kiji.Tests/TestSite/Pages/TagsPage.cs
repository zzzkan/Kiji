using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.Pages;

[Route("/tags/{TagSlug}/")]
public sealed class TagsPage : ComponentBase
{
    [Inject]
    public ContentDictionary<Post> Posts { get; set; } = default!;

    [Parameter]
    public string TagSlug { get; set; } = string.Empty;

    private List<Post> _posts = [];
    private string _tagName = string.Empty;

    protected override void OnParametersSet()
    {
        var tag = Posts.Values
            .SelectMany(static post => post.Tags)
            .Where(tag => string.Equals(tag.UrlSlug, TagSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        _tagName = tag?.Name
            ?? throw new KeyNotFoundException($"Tag '{TagSlug}' was not found.");
        _posts =
        [
            .. Posts.Values
                .Where(post => post.Tags.Any(tagItem => string.Equals(tagItem.UrlSlug, TagSlug, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(static post => post.CreatedAt),
        ];
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PageHead>(0);
        builder.AddAttribute(1, nameof(PageHead.Title), $"Tag: {_tagName}");
        builder.AddAttribute(2, nameof(PageHead.Description), $"Tagとして'{_tagName}'が付けられた記事一覧です。");
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
