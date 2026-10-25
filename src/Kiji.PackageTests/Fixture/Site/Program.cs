using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using PackageSite;

var builder = StaticSite.CreateBuilder(args);
builder.Info = new() { Name = "Package consumer", BaseUrl = new Uri("https://example.test/kiji/") };
builder.Services.AddLocalization();
builder.AddMarkdownContent<Article>();
builder.AddPageParameters<ArticlePage>(services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<Article>>>()
    .Select(entry => new { Slug = Path.GetFileNameWithoutExtension(entry.Key), ContentKey = entry.Key }));

var site = builder.Build();
return await site.RunAsync();
