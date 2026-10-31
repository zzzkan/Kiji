using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using PackageSite;

var builder = StaticSite.CreateBuilder(args);
builder.SiteInfo = new() { Name = "Package consumer", BaseUrl = new Uri("https://example.test/kiji/") };
builder.Services.AddLocalization();
builder.AddKeyedMarkdownContent<Article>("articles");
builder.AddPageParameters<ArticlePage>(services => services
    .GetRequiredKeyedService<ContentDictionary<MarkdownContent<Article>>>("articles")
    .Select(entry => new { Slug = Path.GetFileNameWithoutExtension(entry.Key), ContentKey = entry.Key }));

return await builder.Build().RunAsync();
