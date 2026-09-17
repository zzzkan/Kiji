using Kiji;
using Kiji.Markdown;
using Kiji.Sitemaps;
using KijiSite;
using KijiSite.Components;
using KijiSite.Pages;
using Microsoft.Extensions.DependencyInjection;

await using var app = StaticSite.Create(args);
app.Info = new SiteInfo
{
    BaseUrl = new Uri("SITE_BASE_URL"),
    Name = "SITE_NAME",
    Description = "A static site built with Kiji.",
    Language = "en",
};

app.UseMarkdownContent<PostFrontMatter, Post>(
    select: static content => Post.Create(content),
    key: static post => post.Slug);

app.UseDefaultLayout<MainLayout>();
app.AddStaticPages();
app.UseNotFoundPage<NotFoundPage>();

app.AddPages<PostPage>(static services => services
    .GetRequiredService<ContentDictionary<Post>>()
    .Select(static post => new { post.Value.Slug, ContentKey = post.Key }));

app.AddSitemap();

// Starts the dev server, or generates the site when dotnet publish asks for it.
return await app.RunAsync();
