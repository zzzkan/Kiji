using Kiji;
using Kiji.Docs;
using Kiji.Docs.Components;
using Kiji.Docs.Pages;
using Kiji.Markdown;
using Kiji.Sitemaps;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo
{
    // A GitHub Pages project site, so the base path is /kiji/. Every link written by
    // this site goes through Site.Path so it resolves under that prefix; dev and
    // preview serve there too.
    BaseUrl = new Uri("https://zzzkan.github.io/kiji/"),
    Name = "Kiji",
    Description = "A static site generator framework for .NET. Write pages as Razor components.",
    Language = "en",
    Author = "zzzkan",
};

// Docs live at contents/<slug>/index.md, so images can sit beside the page using them.
var docs = builder.AddMarkdownContent<DocFrontMatter>()
    .WithKey(static doc => doc.FileInfo.RelativeDirectoryPath)
    .OrderBy(static doc => doc.FrontMatter.Order);

await using var app = builder.Build();

app.MapDefaultLayout<MainLayout>();
app.MapPages();
app.MapNotFound<NotFoundPage>();

app.MapRoutes<DocPage, MarkdownContent<DocFrontMatter>>(
    docs,
    static doc => new { Slug = doc.FileInfo.RelativeDirectoryPath });

app.MapSitemap();

return await app.RunAsync();
