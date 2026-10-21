using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite.PageCaching;

[Layout(typeof(AlwaysRenderLayout))]
public sealed class RenderPolicyLayout : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Body);
}
