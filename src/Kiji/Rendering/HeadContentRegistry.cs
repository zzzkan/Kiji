using Kiji.Components;
using Microsoft.AspNetCore.Components;

namespace Kiji.Rendering;

/// <summary>
/// Per-page-render channel connecting <see cref="StaticHeadContent"/> providers to the
/// head outlet in the built-in root document. Registered scoped so each page render
/// gets isolated head state. All members are Dispatcher-confined: they are only
/// called while components render on the renderer's dispatcher, so no locking is
/// needed (the same contract as Blazor's internal SectionRegistry).
/// </summary>
internal sealed class HeadContentRegistry
{
    private readonly List<StaticHeadContent> _providers = [];
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
    /// Providers compose in registration order; updates preserve that order.
    /// </summary>
    public void SetContent(StaticHeadContent provider)
    {
        if (!_providers.Contains(provider))
        {
            _providers.Add(provider);
        }

        _subscriber?.Invoke(CurrentContent);
    }

    /// <summary>
    /// Removes only the disposed provider's contribution.
    /// </summary>
    public void RemoveProvider(StaticHeadContent provider)
    {
        if (_providers.Remove(provider))
        {
            _subscriber?.Invoke(CurrentContent);
        }
    }

    private RenderFragment? CurrentContent
    {
        get
        {
            if (_providers.Count == 0)
            {
                return null;
            }

            if (_providers.Count == 1)
            {
                return _providers[0].ChildContent;
            }

            var fragments = new RenderFragment?[_providers.Count];
            for (var i = 0; i < _providers.Count; i++)
            {
                fragments[i] = _providers[i].ChildContent;
            }

            return builder =>
            {
                foreach (var fragment in fragments)
                {
                    builder.AddContent(0, fragment);
                }
            };
        }
    }
}
