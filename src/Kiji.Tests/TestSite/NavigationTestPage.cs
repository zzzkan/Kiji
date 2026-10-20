using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;

namespace Kiji.Tests.TestSite;

[Route("/navigation/{Id}/")]
public sealed class NavigationTestPage(NavigationManager navigation, SiteInfo site) : ComponentBase
{
    // This API must work even before any property is read.
    private readonly string _relative = navigation.ToBaseRelativePath(site.ResolveUrl("about/").AbsoluteUri);

    [Inject] public NavigationManager Navigation { get; set; } = default!;
    [Inject] public SiteInfo Site { get; set; } = default!;
    [Parameter] public string Id { get; set; } = "";
    [Parameter] public Action<NavigationManager>? Navigate { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // Overlap page renders to catch navigation state shared across scopes.
        await Task.Yield();
        Navigate?.Invoke(Navigation);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddContent(0, Navigation.BaseUri);
        builder.AddContent(1, "|");
        builder.AddContent(2, Navigation.Uri);
        builder.AddContent(3, "|");
        builder.AddContent(4, Navigation.ToBaseRelativePath(Navigation.Uri));
        builder.AddContent(5, "|");
        builder.AddContent(6, Navigation.ToAbsoluteUri("about/").AbsoluteUri);
        builder.AddContent(7, "|" + _relative);

        AddLink(builder, Site.ResolveUrl("navigation/first/").AbsolutePath, NavLinkMatch.All, "first");
        AddLink(builder, Site.ResolveUrl("navigation/").AbsolutePath, NavLinkMatch.Prefix, "parent");
        AddLink(builder, Site.ResolveUrl("navigation/").AbsolutePath, NavLinkMatch.All, "exact");
    }

    private static void AddLink(RenderTreeBuilder builder, string href, NavLinkMatch match, string label)
    {
        builder.OpenRegion(8);
        builder.OpenComponent<NavLink>(0);
        builder.AddAttribute(1, "href", href);
        builder.AddAttribute(2, nameof(NavLink.Match), match);
        builder.AddAttribute(3, nameof(NavLink.ActiveClass), "selected");
        builder.AddAttribute(4, nameof(NavLink.ChildContent), (RenderFragment)(content => content.AddContent(0, label)));
        builder.CloseComponent();
        builder.CloseRegion();
    }
}
