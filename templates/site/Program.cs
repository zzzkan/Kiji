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

// Posts live at contents/<slug>/index.md, so images can sit beside the post using them.
// The directory name becomes FileInfo.Slug, which this source uses as its key.
app.UseMarkdownContent<PostFrontMatter>(key: static post => post.FileInfo.Slug);

app.UseDefaultLayout<MainLayout>();
app.AddStaticPages();
app.UseNotFoundPage<NotFoundPage>();

// Slug is the page's route segment; ContentKey is how the page finds itself in the
// dictionary. They are the same value here, but they are different things.
app.AddPages<PostPage>(static services => services
    .GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
    .Select(static post => new { Slug = post.Key, ContentKey = post.Key }));

app.AddSitemap();

// Starts the dev server, or generates the site when dotnet publish asks for it.
return await app.RunAsync();
