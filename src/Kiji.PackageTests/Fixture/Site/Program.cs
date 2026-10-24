using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using PackageSite;

var site = StaticSite.Create(args);
site.Info = new() { Name = "Package consumer", BaseUrl = new Uri("https://example.test/kiji/") };
site.UseServices(services => services.AddLocalization());
site.UseMarkdownContent<Article>();
site.AddPageParameters<ArticlePage>(services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<Article>>>()
    .Select(entry => new { Slug = Path.GetFileNameWithoutExtension(entry.Key), ContentKey = entry.Key }));
return await site.RunAsync();
