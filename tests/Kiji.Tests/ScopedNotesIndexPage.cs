using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests;

[Route("/notes/{Kind}/")]
public sealed class ScopedNotesIndexPage : ComponentBase
{
    [Inject]
    public ContentCollection<ScopedNote> Notes { get; set; } = default!;

    [Parameter]
    public string Kind { get; set; } = string.Empty;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");
        foreach (var note in Notes)
        {
            builder.OpenRegion(1);
            builder.OpenElement(0, "li");
            builder.AddContent(1, $"{note.Key}: {note.Title}");
            builder.CloseElement();
            builder.CloseRegion();
        }

        builder.CloseElement();
    }
}
