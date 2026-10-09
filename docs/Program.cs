using Kiji;
using Kiji.Docs.Components;
using Kiji.Docs.Models;
using Kiji.Docs.Pages;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;

var site = StaticSite.Create(args);
site.Info = new()
{
    BaseUrl = new Uri("https://kiji-docs.zzzkan.workers.dev/"),
    Name = "Kiji",
    Description = "A static site generator for .NET. Write pages as Razor components.",
    Language = "en",
    Author = "zzzkan",
};

site.UseMarkdownContent<FrontMatter, Article>(
    select: static content => Article.Create(content),
    configure: static options =>
    {
        // Match .shell in wwwroot/css/app.css: 2.5rem horizontal padding,
        // plus a 13rem sidebar and 2.5rem gap above the mobile breakpoint.
        options.ImageSizes = "(max-width: 52rem) calc(100vw - 2.5rem), min(calc(100vw - 18rem), 46rem)";
    });
site.UseDefaultLayout<MainLayout>();
site.UseNotFoundPage<NotFoundPage>();

site.AddStaticPages();
site.AddPages<ArticlePage>(static services => services
    .GetRequiredService<ContentDictionary<Article>>()
    .Select(static doc => new { doc.Value.Slug, ContentKey = doc.Key }));
site.AddSitemap();

return await site.RunAsync();
