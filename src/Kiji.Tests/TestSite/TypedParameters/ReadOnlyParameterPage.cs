using Microsoft.AspNetCore.Components;

namespace Kiji.Tests.TestSite.TypedParameters;

[Route("/readonly/{Id}/")]
public sealed class ReadOnlyParameterPage : ComponentBase
{
    [Parameter] public int Id { get; set; }
#pragma warning disable BL0001 // Deliberately invalid component for planning diagnostics.
    [Parameter] public string Value { get; private set; } = string.Empty;
#pragma warning restore BL0001
}
