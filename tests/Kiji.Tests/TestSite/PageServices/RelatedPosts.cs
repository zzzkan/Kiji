using Kiji.Markdown;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class RelatedPosts(
    ContentCollection<MarkdownContent<FrontMatter>> posts,
    IReadOnlyList<RelatedTag> tags)
{
    public IReadOnlyList<string> GetKeys(string currentKey)
    {
        return [.. (posts[currentKey].FrontMatter.Tags ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(GetTagKeys)
            .Where(key => !StringComparer.OrdinalIgnoreCase.Equals(key, currentKey))
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Path.GetFileNameWithoutExtension(posts[group.Key].Path))];
    }

    private IReadOnlyList<string> GetTagKeys(string name)
    {
        var tag = tags.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return [];
        }
        tag.Probe.Record();
        return tag.Keys;
    }
}
