using Microsoft.AspNetCore.Components;

namespace Kiji.Tests.TestSite;

public sealed class ConstructorNavigationPage : ComponentBase
{
    public ConstructorNavigationPage(NavigationManager navigation)
    {
        _ = navigation;
    }
}
