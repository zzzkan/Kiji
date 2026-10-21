using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[AlwaysRender]
public sealed class AlwaysRenderValue : ComponentBase
{
    [Inject] public Func<string> ReadValue { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ReadValue());
}
