using Kiji.Markdown;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class RelatedPosts(
    ContentDictionary<MarkdownContent<FrontMatter>> posts,
    ContentDictionary<RelatedTag> tags)
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
            .Select(group => Path.GetFileNameWithoutExtension(posts[group.Key].FileInfo.Name))];
    }

    private IReadOnlyList<string> GetTagKeys(string name)
    {
        if (!tags.TryGetValue(name, out var tag))
        {
            return [];
        }
        // Observe actual use without reading another dictionary, which would mask
        // a missing dependency on this cached derived dictionary in the tests.
        tag.Probe.Record();
        return tag.Keys;
    }
}
