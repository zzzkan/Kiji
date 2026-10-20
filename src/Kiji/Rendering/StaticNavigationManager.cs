using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Rendering;

internal sealed class StaticNavigationManager : NavigationManager
{
    public StaticNavigationManager(IServiceProvider services)
    {
        // StaticHtmlRenderer also resolves this for standalone component renders,
        // which have no page URL. Page renders must initialize before any API use.
        if (PageRenderContext.Current is null) { return; }

        var site = services.GetRequiredService<SiteInfo>();
        var page = services.GetRequiredService<PageInfo>();
        Initialize(site.BaseUrl.AbsoluteUri, page.Url.AbsoluteUri);
    }

    protected override void EnsureInitialized()
    {
        throw new InvalidOperationException("NavigationManager is only available during page rendering.");
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        throw new NotSupportedException(
            "NavigationManager.NavigateTo and Refresh are not supported during static rendering. "
            + "Render links or configure redirects in your hosting platform.");
    }
}
