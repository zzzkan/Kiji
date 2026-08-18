using Kiji.Markdown;

namespace Kiji.Tests;

/// <summary>
/// A projected markdown model, used to give a second collection its own element type
/// so both can live in one site.
/// </summary>
public sealed class ScopedNote
{
    private ScopedNote(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }

    public string Title { get; }

    public static ScopedNote Create(MarkdownContent<FrontMatter> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ScopedNote(content.FileInfo.Slug, content.FrontMatter.Title ?? string.Empty);
    }
}
