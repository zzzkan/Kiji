using System.Net;
using System.Text.RegularExpressions;

namespace Kiji.Docs.Models;

/// <summary>Builds a page outline from the headings in rendered Markdown.</summary>
public static partial class Outline
{
    /// <summary>Returns the level 2 and 3 headings that have an ID, in document order.</summary>
    public static IReadOnlyList<OutlineItem> Parse(string html) =>
        [.. Heading().Matches(html).Select(static match => new OutlineItem(
            WebUtility.HtmlDecode(match.Groups["id"].Value),
            WebUtility.HtmlDecode(Tag().Replace(match.Groups["text"].Value, string.Empty)).Trim(),
            int.Parse(match.Groups["level"].ValueSpan)))];

    [GeneratedRegex("""<h(?<level>[23]) id="(?<id>[^"]+)"[^>]*>(?<text>.*?)</h\k<level>>""", RegexOptions.Singleline)]
    private static partial Regex Heading();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}
