using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;

namespace Kiji.Components;

/// <summary>
/// Renders its child content into the document <c>&lt;head&gt;</c> emitted by the
/// built-in root document. Render at most one per page: when several are rendered
/// in the same page, only the most recently rendered one takes effect.
/// </summary>
public sealed class Head : ComponentBase
{
    internal static readonly object SectionId = new();

    /// <summary>
    /// The content placed inside <c>&lt;head&gt;</c>, e.g. the charset meta,
    /// <c>&lt;title&gt;</c>, metas, and links.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<SectionContent>(0);
        builder.AddAttribute(1, nameof(SectionContent.SectionId), SectionId);
        builder.AddAttribute(2, nameof(SectionContent.ChildContent), ChildContent);
        builder.CloseComponent();
    }
}
