using Microsoft.AspNetCore.Components;

namespace Kiji.Rendering;

/// <summary>
/// Minimal <see cref="NavigationManager"/> implementation for static rendering.
/// Registered as a scoped service so each page render gets its own instance,
/// which keeps concurrent renders isolated from each other.
/// </summary>
internal sealed class StaticNavigationManager : NavigationManager
{
    /// <summary>
    /// Initializes the navigation state for a single page render.
    /// May be called only once per instance.
    /// </summary>
    /// <param name="baseUri">The site base URI.</param>
    /// <param name="currentUri">The absolute URI of the page being rendered.</param>
    public void Initialize(Uri baseUri, Uri currentUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(currentUri);

        Initialize(baseUri.AbsoluteUri, currentUri.AbsoluteUri);
    }

    /// <inheritdoc/>
    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        // Static rendering does not support navigation.
    }
}
