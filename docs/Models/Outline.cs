using System.Net;
using System.Text.RegularExpressions;

namespace Kiji.Docs.Models;

public static partial class Outline
{
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
