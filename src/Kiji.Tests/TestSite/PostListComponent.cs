using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

public sealed class PostListComponent : ComponentBase
{
    [Parameter, EditorRequired]
    public IReadOnlyList<Post> Posts { get; set; } = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");

        foreach (var post in Posts)
        {
            builder.OpenRegion(1);
            builder.OpenElement(1, "li");
            builder.OpenElement(2, "a");
            builder.AddAttribute(3, "href", $"/blog/{post.SlugUrlEncoded}/");
            builder.AddContent(4, post.Title);
            builder.CloseElement();
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}
