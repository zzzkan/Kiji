using Kiji.Markdown;

namespace Kiji.Tests.TestSite.PageServices;

public sealed record RelatedTag(string Name, IReadOnlyList<string> Keys, RelatedProbe Probe)
{
    public static IReadOnlyList<RelatedTag> Collect(ContentDictionary<MarkdownContent<FrontMatter>> posts, RelatedProbe probe)
    {
        return [.. posts.SelectMany(post => (post.Value.FrontMatter.Tags ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(tag => (Tag: tag, post.Key)))
            .GroupBy(item => item.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RelatedTag(group.Key, [.. group.Select(item => item.Key)], probe))];
    }
}
