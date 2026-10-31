using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Markdown;

/// <summary>Renders a Markdown body as HTML inside the current page.</summary>
/// <typeparam name="TFrontMatter">The site's front matter model.</typeparam>
public sealed class MarkdownBody<TFrontMatter> : ComponentBase
{
    private string _html = string.Empty;

    /// <summary>The Markdown content to render.</summary>
    [Parameter, EditorRequired]
    public MarkdownContent<TFrontMatter> Content { get; set; } = null!;

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        ArgumentNullException.ThrowIfNull(Content);
        _html = await Content.RenderAsync();
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, _html);
}
