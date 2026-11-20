namespace Kiji;

/// <summary>The page being rendered; injectable only while a page renders.</summary>
public sealed class PageInfo
{
    internal PageInfo(Uri url)
    {
        Url = url;
    }

    /// <summary>The page's published URL, also when the dev server renders it.</summary>
    public Uri Url { get; }
}
