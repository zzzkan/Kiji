namespace Kiji.Markdown;

/// <summary>Which files one Markdown source loads.</summary>
public sealed class MarkdownContentOptions
{
    /// <summary>The directory searched recursively for <c>*.md</c>, relative to the project; <c>content</c> by default.</summary>
    public string Directory { get; set; } = "content";

    /// <summary>Selects files before they load; called one file at a time, in path order. All files by default.</summary>
    public Func<FileInfo, bool>? FileFilter { get; set; }
}
