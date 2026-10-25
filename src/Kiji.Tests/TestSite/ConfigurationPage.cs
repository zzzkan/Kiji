using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;

namespace Kiji.Tests.TestSite;

/// <summary>Writes a configuration value, as a site reading an analytics ID from appsettings.json would.</summary>
[Route("/configuration/")]
public sealed class ConfigurationPage : ComponentBase
{
    [Inject] public IConfiguration Configuration { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, $"title:{Configuration["Site:Title"]}");
    }
}
