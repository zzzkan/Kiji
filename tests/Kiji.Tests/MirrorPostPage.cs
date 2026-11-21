using Microsoft.AspNetCore.Components;

namespace Kiji.Tests;

[Route("/mirror/{Slug}/")]
public sealed class MirrorPostPage : ComponentBase
{
    [Parameter]
    public string Slug { get; set; } = string.Empty;

    [Parameter]
    public string ContentKey { get; set; } = string.Empty;
}
