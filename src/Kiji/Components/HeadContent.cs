using Kiji.Rendering;
using Microsoft.AspNetCore.Components;

namespace Kiji.Components;

/// <summary>Places child content in the generated document head.</summary>
/// <remarks>When several instances render on one page, the most recently rendered instance takes effect.</remarks>
public sealed class HeadContent : IComponent, IDisposable
{
    [Inject]
    internal HeadContentRegistry Registry { get; set; } = default!;

    /// <summary>
    /// The content placed inside <c>&lt;head&gt;</c>, e.g. the charset meta,
    /// <c>&lt;title&gt;</c>, metas, and links.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    void IComponent.Attach(RenderHandle renderHandle)
    {
        // Renders nothing in place; the content executes under the head outlet.
    }

    Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        Registry.SetContent(this);
        return Task.CompletedTask;
    }

    void IDisposable.Dispose()
    {
        Registry.RemoveProvider(this);
    }
}
