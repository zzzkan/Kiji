using Kiji.Rendering;
using Microsoft.AspNetCore.Components;

namespace Kiji.Components;

/// <summary>
/// Renders its child content into the document <c>&lt;head&gt;</c> emitted by the
/// built-in root document; Kiji's JavaScript-free replacement for Blazor's
/// <c>Microsoft.AspNetCore.Components.Web.HeadContent</c>. Render at most one per
/// page: when several are rendered in the same page, only the most recently
/// rendered one (last in document order) takes effect.
/// </summary>
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
