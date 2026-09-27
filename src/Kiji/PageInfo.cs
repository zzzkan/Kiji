namespace Kiji;

/// <summary>Read-only information about the page being rendered.</summary>
/// <remarks>
/// One instance is shared by the page, layouts, child components, and page services
/// within a render. It is unavailable outside page rendering.
/// </remarks>
public sealed class PageInfo
{
    internal PageInfo(Uri url)
    {
        Url = url;
    }

    /// <summary>
    /// The absolute public URL of this page, resolved against <see cref="SiteInfo.BaseUrl"/>.
    /// Development renders use the published URL, not the local request URL.
    /// </summary>
    public Uri Url { get; }
}
