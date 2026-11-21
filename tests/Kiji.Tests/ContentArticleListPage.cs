using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

[Route("/article-list/{Kind}/")]
public sealed class ContentArticleListPage : ComponentBase
{
    [Inject] public ContentCollection<ContentArticle> Articles { get; set; } = default!;
    [Inject] public ContentRenderLog Log { get; set; } = default!;
    [Parameter] public string Kind { get; set; } = "metadata";

    private ContentArticle[] _articles = [];
    private string _body = "";
    private int _count;

    protected override async Task OnParametersSetAsync()
    {
        Log.Record("list-" + Kind);
        _count = Articles.Count;
        if (Kind != "count") { _articles = [.. Articles]; }
        if (Kind == "render")
        {
            foreach (var article in _articles) { _body += await article.Content.RenderAsync(); }
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, _count);
        foreach (var article in _articles)
        {
            builder.OpenRegion(1);
            builder.AddContent(0, Kind == "front-matter" ? article.Content.FrontMatter.Title : article.Title);
            builder.AddMarkupContent(1, article.Excerpt);
            builder.CloseRegion();
        }
        builder.AddMarkupContent(2, _body);
    }
}
