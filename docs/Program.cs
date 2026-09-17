using Kiji;
using Kiji.Docs.Models;
using Kiji.Docs.Components;
using Kiji.Docs.Pages;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;

await using var site = StaticSite.Create(args);
site.Info = new()
{
    BaseUrl = new Uri("https://zzzkan.github.io/kiji/"),
    Name = "Kiji",
    Description = "A static site generator framework for .NET. Write pages as Razor components.",
    Language = "en",
    Author = "zzzkan",
};

site.UseMarkdownContent<DocFrontMatter, Doc>(
    select: static content => Doc.Create(content),
    key: static doc => doc.Slug);
site.UseDefaultLayout<MainLayout>();
site.UseNotFoundPage<NotFoundPage>();

site.AddStaticPages();
site.AddPages<DocPage>(static services => services
    .GetRequiredService<ContentDictionary<Doc>>()
    .Select(static doc => new { doc.Value.Slug, ContentKey = doc.Key }));
site.AddSitemap();

return await site.RunAsync();
