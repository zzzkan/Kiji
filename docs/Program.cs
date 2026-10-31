using Kiji;
using Kiji.Docs.Layout;
using Kiji.Docs.Models;
using Kiji.Docs.Pages;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Microsoft.Extensions.DependencyInjection;

var builder = StaticSite.CreateBuilder(args);
builder.SiteInfo = new()
{
    BaseUrl = new Uri("https://kiji-docs.zzzkan.workers.dev/"),
    Name = "Kiji",
    Description = "Write Razor. Publish a purely static site. Kiji brings your .NET components and Markdown together in plain HTML, ready for any static host.",
    Language = "en",
    Author = "zzzkan",
};

builder.AddMarkdownContent<FrontMatter, Article>(
    select: static content => Article.Create(content));
builder.UseDefaultLayout<MainLayout>();
builder.UseNotFoundPage<NotFoundPage>();
builder.AddPageParameters<ArticlePage>(static services =>
{
    var articles = services.GetRequiredService<ContentCollection<Article>>()
        .OrderBy(static article => article.Order).ToArray();
    return articles.Select((article, index) => new
    {
        article.Slug,
        Article = article,
        Previous = index > 0 ? articles[index - 1] : null,
        Next = index + 1 < articles.Length ? articles[index + 1] : null,
    });
});
builder.AddSitemap();

return await builder.Build().RunAsync();
