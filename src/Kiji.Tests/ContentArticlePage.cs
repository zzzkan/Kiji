using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

[Route("/articles/{Slug}/")]
public sealed class ContentArticlePage : ComponentBase
{
    [Inject] public ContentRenderLog Log { get; set; } = default!;
    [Parameter] public string Slug { get; set; } = "";
    [Parameter] public ContentArticle Article { get; set; } = default!;
    [Parameter] public ContentArticle? Previous { get; set; }
    [Parameter] public ContentArticle? Next { get; set; }
    [Parameter] public bool RenderBody { get; set; }
    [Parameter] public bool ShowNavigation { get; set; }
    [Parameter] public IReadOnlyList<ContentArticle>? Items { get; set; }

    private string _body = "";

    protected override async Task OnParametersSetAsync()
    {
        Log.Record(Slug);
        if (RenderBody) { _body = await Article.Content.RenderAsync(); }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Article.Title);
        builder.AddContent(1, $"|previous:{Previous?.Slug}|next:{Next?.Slug}|");
        builder.AddMarkupContent(2, _body);
        if (ShowNavigation)
        {
            builder.OpenComponent<ContentNavigation>(3);
            builder.CloseComponent();
        }
    }
}
