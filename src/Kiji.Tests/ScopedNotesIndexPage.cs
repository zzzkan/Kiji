using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

/// <summary>
/// Enumerates a second markdown collection, so a build can assert that editing content
/// in one collection's directory leaves pages that only read another's untouched.
/// </summary>
/// <remarks>
/// The route takes a parameter purely so tests that do not exercise it can neutralize
/// the page with an empty route set, the way the other test-only templates are handled.
/// </remarks>
[Route("/notes/{Kind}/")]
public sealed class ScopedNotesIndexPage : ComponentBase
{
    [Inject]
    public ContentDictionary<ScopedNote> Notes { get; set; } = default!;

    [Parameter]
    public string Kind { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");
        foreach (var (key, note) in Notes)
        {
            builder.OpenRegion(1);
            builder.OpenElement(0, "li");
            builder.AddContent(1, $"{key}: {note.Title}");
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}
