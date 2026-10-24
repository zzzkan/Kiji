using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using PackageSite;

var site = StaticSite.Create(args);
site.Info = new() { Name = "Package consumer", BaseUrl = new Uri("https://example.test/kiji/") };
site.UseServices(services => services.AddLocalization());
site.UseMarkdownContent<Article>();
site.AddPageParameters<Details>(_ => [new { Slug = "first", Count = 7 }]);
return await site.RunAsync();
