using Kiji.Components;
using Microsoft.AspNetCore.Components;

namespace Kiji.Rendering;

/// <summary>
/// Per-page-render channel connecting <see cref="HeadContent"/> providers to the
/// head outlet in the built-in root document. Registered scoped so each page render
/// gets isolated head state. All members are Dispatcher-confined: they are only
/// called while components render on the renderer's dispatcher, so no locking is
/// needed (the same contract as Blazor's internal SectionRegistry).
/// </summary>
internal sealed class HeadContentRegistry
{
    private readonly List<HeadContent> _providers = [];
    private Action<RenderFragment?>? _subscriber;

    /// <summary>
    /// Attaches the outlet's change callback and immediately pushes the current
    /// content so the outlet renders whatever has been provided so far (or empty).
    /// </summary>
    public void Subscribe(Action<RenderFragment?> subscriber)
    {
        if (_subscriber is not null)
        {
            throw new InvalidOperationException("A head outlet is already attached for this page render.");
        }

        _subscriber = subscriber;
        subscriber(CurrentContent);
    }

    /// <summary>
    /// Detaches the outlet's change callback.
    /// </summary>
    public void Unsubscribe()
    {
        _subscriber = null;
    }

    /// <summary>
    /// Adds the provider on first call and republishes its content on updates.
    /// The most recently added provider wins, matching document render order.
    /// </summary>
    public void SetContent(HeadContent provider)
    {
        if (!_providers.Contains(provider))
        {
            _providers.Add(provider);
        }

        if (ReferenceEquals(_providers[^1], provider))
        {
            _subscriber?.Invoke(provider.ChildContent);
        }
    }

    /// <summary>
    /// Removes a disposed provider; if it was the current one, the previously
    /// rendered provider's content takes effect again.
    /// </summary>
    public void RemoveProvider(HeadContent provider)
    {
        var wasCurrent = _providers.Count > 0 && ReferenceEquals(_providers[^1], provider);
        _providers.Remove(provider);

        if (wasCurrent)
        {
            _subscriber?.Invoke(CurrentContent);
        }
    }

    private RenderFragment? CurrentContent => _providers.Count > 0 ? _providers[^1].ChildContent : null;
}
