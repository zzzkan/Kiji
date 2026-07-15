using Kiji.Markdown;

namespace Kiji.SyntheticSite;

/// <summary>
/// Derives the route slug of a synthetic post from its markdown file location
/// (posts live at <c>contents/&lt;slug&gt;/index.md</c>).
/// </summary>
public static class PostSlug
{
    public static string From(MarkdownFileInfo fileInfo)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);

        return fileInfo.RelativeDirectoryPath;
    }
}
