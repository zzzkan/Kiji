namespace Kiji.Markdown;

/// <summary>File selection for one Markdown content source.</summary>
public sealed class MarkdownContentOptions
{
    /// <summary>The directory to scan recursively for <c>*.md</c>. Absolute or relative to the site project directory; defaults to <c>content</c>.</summary>
    public string Directory { get; set; } = "content";

    /// <summary>An optional file filter applied before loading, defaulting to all Markdown files.</summary>
    public Func<FileInfo, bool>? FileFilter { get; set; }
}
