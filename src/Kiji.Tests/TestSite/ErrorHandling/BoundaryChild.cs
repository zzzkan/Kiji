using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;

namespace Kiji.Tests.TestSite.ErrorHandling;

public sealed partial class BoundaryChild : ComponentBase
{
    [Inject] public ILogger<BoundaryChild> Logger { get; set; } = null!;
    [Parameter] public string Key { get; set; } = string.Empty;
    [Parameter] public string Failure { get; set; } = string.Empty;

    protected override void OnInitialized()
    {
        Initializing(Logger, Key);
        if (Failure == "sync") { throw CreateException(); }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Failure == "async")
        {
            await Task.Yield();
            throw CreateException();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Failure == "render") { throw CreateException(); }
        builder.AddContent(0, $"Healthy {Key}");
    }

    private InvalidOperationException CreateException() => new($"Private failure for {Key}");

    [LoggerMessage(1, LogLevel.Information, "Initializing {Key}")]
    private static partial void Initializing(ILogger logger, string key);
}
