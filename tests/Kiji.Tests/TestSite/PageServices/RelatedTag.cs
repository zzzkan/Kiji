using Kiji.Markdown;

namespace Kiji.Tests.TestSite.PageServices;

public sealed record RelatedTag(string Name, IReadOnlyList<string> Keys, RelatedProbe Probe)
{
    public static IReadOnlyList<RelatedTag> Collect(ContentCollection<MarkdownContent<FrontMatter>> posts, RelatedProbe probe)
    {
        return [.. posts.SelectMany(post => (post.FrontMatter.Tags ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(tag => (Tag: tag, post.Path)))
            .GroupBy(item => item.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RelatedTag(group.Key, [.. group.Select(item => item.Path)], probe))];
    }
}
