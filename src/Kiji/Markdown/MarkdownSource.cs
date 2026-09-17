namespace Kiji.Markdown;

/// <summary>
/// Everything obtained from a single read of a markdown file: the parsed front
/// matter, the body (front matter stripped), and the content hash of the raw bytes.
/// </summary>
internal sealed record MarkdownSource<TFrontMatter>(
    TFrontMatter FrontMatter,
    string Body,
    string ContentHash);
