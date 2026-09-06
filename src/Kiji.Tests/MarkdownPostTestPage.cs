using Kiji.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

/// <summary>
/// Minimal markdown-backed page used to exercise the full markdown + image pipeline
/// end to end through <see cref="KijiApp.PublishSiteAsync"/>.
/// </summary>
[Route("/md/{Slug}/")]
public sealed class MarkdownPostTestPage : ComponentBase
{
    [Inject]
    public ContentDictionary<MarkdownContent<FrontMatter>> Posts { get; set; } = default!;

    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Parameter]
    public string ContentKey { get; set; } = string.Empty;

    private string _html = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _html = await Posts[ContentKey].RenderAsync();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, _html);
    }
}
