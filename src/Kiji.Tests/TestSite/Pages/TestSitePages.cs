namespace Kiji.Tests.TestSite.Pages;

/// <summary>
/// The test site's page components, used as the expected set in page
/// discovery tests.
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
