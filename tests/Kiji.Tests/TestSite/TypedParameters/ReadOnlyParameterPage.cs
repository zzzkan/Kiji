using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace Kiji.Tests.TestSite.TypedParameters;

[Route("/readonly/{Id}/")]
public sealed class ReadOnlyParameterPage : ComponentBase
{
    [Parameter] public int Id { get; set; }
    [SuppressMessage("Usage", "BL0001", Justification = "Invalid on purpose: planning must reject it.")]
    [Parameter] public string Value { get; private set; } = string.Empty;
}
