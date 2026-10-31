using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

public sealed class ContentNavigation : ComponentBase
{
    [Inject] public ContentCollection<ContentArticle> Articles { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        foreach (var article in Articles)
        {
            builder.OpenRegion(0);
            builder.OpenElement(0, "a");
            builder.AddAttribute(1, "href", $"/articles/{article.Slug}/");
            builder.AddContent(2, article.Content.FrontMatter.Title);
            builder.CloseElement();
            builder.CloseRegion();
        }
    }
}
