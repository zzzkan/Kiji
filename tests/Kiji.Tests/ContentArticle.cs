using Kiji.Markdown;

namespace Kiji.Tests;

public sealed record ContentArticle(string Slug, string Title, MarkdownContent<FrontMatter> Content, string? Excerpt = null)
{
    public static ContentArticle Create(MarkdownContent<FrontMatter> content) =>
        new(Path.GetFileNameWithoutExtension(content.Path), content.FrontMatter.Title ?? "", content);
}
