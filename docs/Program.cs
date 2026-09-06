using Kiji;
using Kiji.Docs;
using Kiji.Docs.Components;
using Kiji.Docs.Pages;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    // A GitHub Pages project site, so the base path is /kiji/. Every link written by
    // this site goes through Site.Path so it resolves under that prefix; dev and
    // the dev server serves there too.
    BaseUrl = new Uri("https://zzzkan.github.io/kiji/"),
    Name = "Kiji",
    Description = "A static site generator framework for .NET. Write pages as Razor components.",
    Language = "en",
    Author = "zzzkan",
};

// Docs live at contents/<slug>/index.md, so images can sit beside the page using them.
// The directory name becomes FileInfo.Slug, which this source uses as its key.
builder.AddMarkdownContent<DocFrontMatter>(key: static doc => doc.FileInfo.Slug);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

// Slug is the page's route segment; ContentKey is how the page finds itself in the
// dictionary. They happen to be the same value here, but they are different things.
app.MapRoutes<DocPage>(static services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<DocFrontMatter>>>()
    .Select(static doc => new { Slug = doc.Key, ContentKey = doc.Key }));

app.MapSitemap();

return await app.RunAsync();
