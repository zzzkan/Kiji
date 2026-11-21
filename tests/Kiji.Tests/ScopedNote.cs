using Kiji.Markdown;

namespace Kiji.Tests;

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

        return new ScopedNote(Path.GetFileNameWithoutExtension(content.Path), content.FrontMatter.Title ?? string.Empty);
    }
}
