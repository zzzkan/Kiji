namespace Kiji.Markdown;

/// <summary>
/// Everything obtained from a single read of a markdown file: the parsed front
/// matter, the body (front matter stripped), the raw-byte hash and the YAML text digest.
/// </summary>
internal sealed record MarkdownSource<TFrontMatter>(
    TFrontMatter FrontMatter,
    string Body,
    string ContentHash,
    string FrontMatterDigest);
