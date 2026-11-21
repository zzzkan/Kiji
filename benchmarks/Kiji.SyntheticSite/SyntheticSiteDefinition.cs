using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.SyntheticSite.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kiji.SyntheticSite;

public static class SyntheticSiteDefinition
{
    public static void Configure(IHostApplicationBuilder builder)
    {
        builder.UseSiteInfo(new SiteInfo
        {
            BaseUrl = new Uri("https://bench.example.com/"),
            Name = "Kiji Bench",
            Description = "Synthetic benchmark site.",
            Language = "en",
            Author = "bench",
        });

        builder.AddMarkdownContent<PostFrontMatter>(static options => options.Directory = "contents");
        builder.UseDefaultLayout<MainLayout>();
        builder.UseNotFoundPage<NotFoundPage>();

        builder.AddPageParameters<PostPage>(static services => services
            .GetRequiredService<ContentCollection<MarkdownContent<PostFrontMatter>>>()
            .Select(static post => new { Slug = PostSlug.From(post.Path), ContentKey = post.Path }));
        builder.AddRssFeed(static services => services
            .GetRequiredService<ContentCollection<MarkdownContent<PostFrontMatter>>>()
            .OrderByDescending(static post => post.FrontMatter.CreatedAt)
            .Select(static post => new FeedItem(
                post.FrontMatter.Title ?? string.Empty,
                post.FrontMatter.Description ?? string.Empty,
                post.FrontMatter.CreatedAt ?? DateTimeOffset.UnixEpoch,
                RelativePath: $"blog/{PostSlug.From(post.Path)}/")));
        builder.AddSitemap();
    }
}
