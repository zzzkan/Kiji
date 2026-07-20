namespace Kiji.Markdown;

/// <summary>
/// Everything obtained from a single read of a markdown file: the parsed front
/// matter, the body (front matter stripped), the content hash of the raw bytes,
/// and the file stamp the hash corresponds to.
/// </summary>
internal sealed record MarkdownSource<TFrontMatter>(
    TFrontMatter FrontMatter,
    string Body,
    string ContentHash,
    long Length,
    DateTime LastWriteTimeUtc);
