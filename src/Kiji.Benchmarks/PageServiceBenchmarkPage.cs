using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Benchmarks;

public sealed class PageServiceBenchmarkPage : ComponentBase
{
    [Inject] public PageServiceBenchmarkHelper Helper { get; set; } = null!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "h1");
        builder.AddContent(1, Helper.Title);
        builder.CloseElement();
    }
}
