namespace Kiji.Tests.TestSite.Pages;

/// <summary>
/// The explicit page registration list for the test site, as passed to
/// <see cref="KijiApp.MapPages"/>.
/// </summary>
public static class TestSitePages
{
    public static readonly Type[] All =
    [
        typeof(HomePage),
        typeof(AboutPage),
        typeof(PrivacyPolicyPage),
        typeof(NotFoundPage),
        typeof(BlogIndexPage),
        typeof(PostPage),
        typeof(TagsPage),
    ];
}
