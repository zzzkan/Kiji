using Kiji.Rendering;
using Microsoft.AspNetCore.Components;

namespace Kiji.Components;

/// <summary>
/// Renders, inside the root document's <c>&lt;head&gt;</c>, the content provided by
/// the page's <see cref="StaticHeadContent"/>. The outlet renders before the page body, so
/// it starts empty and re-renders when content is published; the queued re-render is
/// processed before the renderer reaches quiescence, all on the renderer's dispatcher.
/// </summary>
internal sealed class StaticHeadOutlet : IComponent, IDisposable
{
    private RenderHandle _renderHandle;
    private RenderFragment? _content;
    private bool _subscribed;
    private bool _disposed;

    [Inject]
    internal HeadContentRegistry Registry { get; set; } = default!;

    public void Attach(RenderHandle renderHandle)
    {
        _renderHandle = renderHandle;
    }

    public Task SetParametersAsync(ParameterView parameters)
    {
        if (!_subscribed)
        {
            Registry.Subscribe(OnContentChanged);
            _subscribed = true;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_subscribed)
        {
            Registry.Unsubscribe();
        }
    }

    private void OnContentChanged(RenderFragment? content)
    {
        _content = content;

        // No re-render once disposal has begun (e.g. a StaticHeadContent provider being
        // removed during renderer teardown); the renderer itself also disregards
        // render requests after it is disposed.
        if (!_disposed)
        {
            _renderHandle.Render(builder => builder.AddContent(0, _content));
        }
    }
}
