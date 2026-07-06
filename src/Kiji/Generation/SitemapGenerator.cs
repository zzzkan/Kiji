using System.Text;

namespace Kiji.Generation;

/// <summary>
/// Generates a sitemap.xml from a list of URL paths.
/// </summary>
public static class SitemapGenerator
{
    /// <summary>
    /// Generates a sitemap.xml file.
    /// </summary>
    /// <param name="outputDirectory">The output directory where <c>sitemap.xml</c> will be written.</param>
    /// <param name="baseUrl">The base URL of the site.</param>
    /// <param name="urls">Relative URL paths to include (e.g. <c>/</c>, <c>/blog/</c>).</param>
    public static async Task GenerateAsync(string outputDirectory, Uri baseUrl, IReadOnlyList<string> urls)
    {
        var path = Path.Combine(outputDirectory, "sitemap.xml");
        await File.WriteAllTextAsync(path, BuildXml(baseUrl, urls), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Generated: {path}");
    }

    internal static string BuildXml(Uri baseUrl, IReadOnlyList<string> urls)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var url in urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseUrl.AppendRelativePath(url).AbsoluteUri}</loc>");
            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }
}
