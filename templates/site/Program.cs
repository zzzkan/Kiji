using Kiji;
using Kiji.Markdown;
using Kiji.Sitemaps;
using KijiSite;
using KijiSite.Components;
using KijiSite.Pages;
using Microsoft.Extensions.DependencyInjection;

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
// The directory name becomes FileInfo.Slug, which this source uses as its key.
builder.AddMarkdownContent<PostFrontMatter>(key: static post => post.FileInfo.Slug);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

// Slug is the page's route segment; ContentKey is how the page finds itself in the
// dictionary. They are the same value here, but they are different things.
app.MapRoutes<PostPage>(static services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
    .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));

app.MapSitemap();

// build (default) | dev [--port <n>] | preview [--port <n>] | clean
return await app.RunAsync();
