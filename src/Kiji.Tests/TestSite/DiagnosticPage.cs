using Kiji.Tests.TestSite.ErrorHandling;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite;

/// <summary>Independently controls logged messages and a caught rendering exception.</summary>
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
        if (Switch.Fail)
        {
            builder.OpenComponent<ErrorBoundaryComponent>(0);
            builder.CloseComponent();
        }
        else
        {
            builder.AddContent(1, "diagnostic");
        }
    }

    [LoggerMessage(Message = "The diagnostic page has a problem.")]
    private static partial void LogProblem(ILogger logger, LogLevel level);
}
