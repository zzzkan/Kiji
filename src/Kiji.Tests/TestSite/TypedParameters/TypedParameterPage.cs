using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.TypedParameters;

[Route("/typed/{Id}/")]
public sealed class TypedParameterPage : ComponentBase
{
    [Inject] public ContentDictionary<ParameterRenderLog> Logs { get; set; } = default!;
    [Parameter] public int Id { get; set; }
    [Parameter] public bool Featured { get; set; }
    [Parameter] public DayOfWeek Kind { get; set; }
    [Parameter] public int? Count { get; set; }
    [Parameter] public string? Summary { get; set; }
    [Parameter] public IReadOnlyList<string>? Tags { get; set; }
    [Parameter] public object? Model { get; set; }
    [Parameter] public IDisposable? Resource { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Logs.Values.Single().Record(this);
        builder.OpenElement(0, "p");
        builder.AddContent(1, FormattableString.Invariant($"{Id}|{Featured}|{Kind}|{Count}|{Summary}|{string.Join(',', Tags ?? [])}|{(Model as ParameterModel)?.Title}"));
        builder.CloseElement();
    }
}
