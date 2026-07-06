using System.Text;

namespace Kiji.Generation;

/// <summary>
/// Generates an RSS 2.0 feed from a list of posts.
/// </summary>
public static class FeedGenerator
{
    /// <summary>
    /// Generates an RSS 2.0 feed XML file.
    /// </summary>
    /// <param name="outputDirectory">The output directory where <c>feed.xml</c> will be written.</param>
    /// <param name="entries">The entries to include in the feed.</param>
    /// <param name="baseUrl">The base URL of the site.</param>
    /// <param name="siteTitle">The title of the site.</param>
    /// <param name="siteDescription">A short description of the site.</param>
    /// <param name="language">The language of the feed (e.g. <c>ja</c>).</param>
    public static async Task GenerateAsync(
        string outputDirectory,
        IReadOnlyList<FeedEntry> entries,
        Uri baseUrl,
        string siteTitle,
        string siteDescription,
        string language)
    {
        var path = Path.Combine(outputDirectory, "feed.xml");
        await File.WriteAllTextAsync(
            path,
            BuildXml(entries, baseUrl, siteTitle, siteDescription, language),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Generated: {path}");
    }

    internal static string BuildXml(
        IReadOnlyList<FeedEntry> entries,
        Uri baseUrl,
        string siteTitle,
        string siteDescription,
        string language)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("  <channel>");
        sb.AppendLine($"    <title>{siteTitle}</title>");
        sb.AppendLine($"    <link>{baseUrl.AbsoluteUri}</link>");
        sb.AppendLine($"    <description>{siteDescription}</description>");
        sb.AppendLine($"    <language>{language}</language>");
        sb.AppendLine($"    <atom:link href=\"{new Uri(baseUrl, "feed.xml").AbsoluteUri}\" rel=\"self\" type=\"application/rss+xml\" />");

        foreach (var entry in entries)
        {
            var postUrl = new Uri(baseUrl, entry.RoutePath.TrimStart('/')).AbsoluteUri;
            var pubDate = entry.PublishedAt.ToString("ddd, dd MMM yyyy HH:mm:ss zz00");

            sb.AppendLine("    <item>");
            sb.AppendLine($"      <title><![CDATA[{entry.Title}]]></title>");
            sb.AppendLine($"      <link>{postUrl}</link>");
            sb.AppendLine($"      <guid>{postUrl}</guid>");
            sb.AppendLine($"      <pubDate>{pubDate}</pubDate>");
            sb.AppendLine($"      <description><![CDATA[{entry.Description}]]></description>");
            sb.AppendLine("    </item>");
        }

        sb.AppendLine("  </channel>");
        sb.AppendLine("</rss>");
        return sb.ToString();
    }
}
