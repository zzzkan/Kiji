using Kiji;
using Kiji.Markdown;
using Kiji.Sitemaps;
using KijiSite;
using KijiSite.Components;
using KijiSite.Pages;

// This template is deliberately minimal: three pages, one markdown collection, and a
// sitemap. RSS feeds, images, tag pages, and custom markdown pipelines are all supported
// but left out — see https://zzzkan.github.io/kiji/docs/ for how to add them.

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    BaseUrl = new Uri("SITE_BASE_URL"),
    Name = "SITE_NAME",
    Description = "A static site built with Kiji.",
    Language = "en",
};

// Posts live at contents/<slug>/index.md, so images can sit beside the post using them.
// The directory name becomes the slug.
var posts = builder.AddMarkdownContent<PostFrontMatter>()
    .WithKey(static post => post.FileInfo.RelativeDirectoryPath)
    .OrderByDescending(static post => post.FrontMatter.CreatedAt);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

app.MapRoutes<PostPage, MarkdownContent<PostFrontMatter>>(
    posts,
    static post => new { Slug = post.FileInfo.RelativeDirectoryPath });

app.MapSitemap();

// build (default) | dev [--port <n>] | preview [--port <n>] | clean
return await app.RunAsync();
