using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kiji.SyntheticSite;

/// <summary>
/// Materializes a deterministic synthetic site (markdown contents, static assets,
/// and optional images) under a root directory.
/// </summary>
public static class SyntheticSiteWriter
{
    private static readonly string[] TagPool =
    [
        "dotnet", "csharp", "blazor", "performance", "web",
        "markdown", "tooling", "testing", "design", "release",
    ];

    public static async Task WriteAsync(string root, int pages, bool includeImages)
    {
        var contentsDir = Path.Combine(root, "contents");
        var staticDir = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(contentsDir);
        Directory.CreateDirectory(Path.Combine(staticDir, "css"));

        await File.WriteAllTextAsync(
            Path.Combine(staticDir, "css", "app.css"),
            "body { font-family: sans-serif; margin: 0 auto; max-width: 48rem; }\n");
        for (var i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(staticDir, $"asset-{i:D2}.txt"),
                $"static asset {i}\n{new string('x', 2048)}\n");
        }

        for (var i = 0; i < pages; i++)
        {
            var slug = $"post-{i:D5}";
            var postDir = Path.Combine(contentsDir, slug);
            Directory.CreateDirectory(postDir);

            var withImage = includeImages && i % 10 == 0;
            if (withImage)
            {
                await WriteImageAsync(Path.Combine(postDir, "cover.png"), 640, 360, seed: i);
            }

            await File.WriteAllTextAsync(Path.Combine(postDir, "index.md"), BuildMarkdown(i, slug, withImage));
        }
    }

    private static string BuildMarkdown(int index, string slug, bool withImage)
    {
        var createdAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index % 730);
        var tagA = TagPool[index % TagPool.Length];
        var tagB = TagPool[(index / TagPool.Length + 1) % TagPool.Length];

        var builder = new StringBuilder(4096);
        builder.Append(CultureInfo.InvariantCulture, $"""
            ---
            title: Synthetic Post {index:D5}
            description: Deterministic benchmark post number {index} exercising the markdown pipeline.
            createdAt: {createdAt:yyyy-MM-dd}
            tags: [{tagA}, {tagB}]
            ---

            # Synthetic Post {index:D5}

            This is a deterministic post used to measure the Kiji build pipeline.
            It contains headings, emphasis, links, lists, and a code block so the
            markdown work is representative of a real blog article.

            """);

        if (withImage)
        {
            builder.Append("""
                ![Cover image](cover.png)

                """);
        }

        builder.Append(CultureInfo.InvariantCulture, $"""
            ## Section one

            Post `{slug}` links to [the home page](/) and to [another post](/blog/post-00000/).
            Some *emphasis*, some **strong text**, and some `inline code` follow.

            - First bullet with a bit of text to fill the line out nicely
            - Second bullet mentioning {tagA} and {tagB}
            - Third bullet, because lists usually have at least three items

            ## Section two

            ```csharp
            var builder = KijiApp.CreateBuilder(args); // post {index}
            await using var app = builder.Build();
            return await app.RunAsync();
            ```

            1. Ordered item one
            2. Ordered item two
            3. Ordered item three

            ## Section three

            A closing paragraph long enough to be realistic. The quick brown fox jumps
            over the lazy dog while the static site generator renders razor components
            to HTML at index {index}. Repetition pads the document toward a typical
            article size without being fully compressible.

            > A block quote for good measure, citing absolutely nobody.

            Final paragraph with a [reference link](https://example.com/docs/{index})
            and a trailing sentence to end the document.
            """);

        return builder.ToString();
    }

    private static async Task WriteImageAsync(string path, int width, int height, int seed)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = new Rgba32((byte)((x + seed) % 255), (byte)((y + seed) % 255), (byte)(seed % 255));
            }
        }

        await image.SaveAsPngAsync(path);
    }
}
