using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using PackageSite;

var builder = StaticSite.CreateBuilder(args);
builder.SiteInfo = new() { Name = "Package consumer", BaseUrl = new Uri("https://example.test/kiji/") };
builder.Services.AddLocalization();
builder.AddMarkdownContent<Article>();
builder.AddPageParameters<ArticlePage>(services => services
    .GetRequiredService<ContentCollection<MarkdownContent<Article>>>()
    .Select(entry => new { Slug = Path.GetFileNameWithoutExtension(entry.Path), ContentKey = entry.Path }));

return await builder.Build().RunAsync();
