using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite;

/// <summary>Logs at the switch's level while rendering, as a page with a problem would.</summary>
[Route("/diagnostic/")]
public sealed partial class DiagnosticPage : ComponentBase
{
    [Inject] public ILogger<DiagnosticPage> Logger { get; set; } = default!;
    [Inject] public DiagnosticSwitch Switch { get; set; } = default!;

    protected override void OnInitialized()
    {
        Switch.Renders++;
        if (Switch.Level is { } level)
        {
            LogProblem(Logger, level);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, "diagnostic");
    }

    [LoggerMessage(Message = "The diagnostic page has a problem.")]
    private static partial void LogProblem(ILogger logger, LogLevel level);
}
