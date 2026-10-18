using Kiji;
using Kiji.Docs.Layout;
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
    Description = "Write Razor. Publish a purely static site. Kiji brings your .NET components and Markdown together in plain HTML, ready for any static host.",
    Language = "en",
    Author = "zzzkan",
};

site.UseMarkdownContent<FrontMatter, Article>(
    select: static content => Article.Create(content));
site.UseDefaultLayout<MainLayout>();
site.AddNotFoundPage<NotFoundPage>();

site.AddPages();
site.AddPages<ArticlePage>(static services => services
    .GetRequiredService<ContentDictionary<Article>>()
    .Select(static doc => new { doc.Value.Slug, ContentKey = doc.Key }));
site.AddSitemap();

return await site.RunAsync();
