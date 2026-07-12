using Microsoft.AspNetCore.Components;

namespace Kiji.Tests;

/// <summary>
/// Dynamic page fixture that mirrors blog posts under a second route, used to
/// exercise content-identity collisions across pages. Public so the assembly
/// scan can discover it; tests neutralize its template with an empty route set.
/// </summary>
[Route("/mirror/{Slug}/")]
public sealed class MirrorPostPage : ComponentBase
{
    [Parameter]
    public string Slug { get; set; } = string.Empty;
}
