using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

public sealed class RenderPolicyWrapper : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DynamicComponent>(0);
        builder.AddComponentParameter(1, nameof(DynamicComponent.Type), typeof(DerivedVolatileComponent));
        builder.CloseComponent();
    }
}
