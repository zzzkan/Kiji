using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Kiji.Tests.TestSite;

/// <summary>Changes a live source after planning, before reading configuration and bound options.</summary>
[Route("/snapshot/")]
public sealed class ConfigurationSnapshotPage : ComponentBase
{
    [Inject] public IConfiguration Configuration { get; set; } = default!;
    [Inject] public IOptions<FeatureOptions> Options { get; set; } = default!;
    [Inject] public Action BeforeRead { get; set; } = default!;

    protected override void OnInitialized() => BeforeRead();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"title:{Configuration["Site:Title"]};enabled:{Options.Value.Enabled}");
    }
}
