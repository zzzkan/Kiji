namespace Kiji.Markdown;

internal sealed record MarkdownSource<TFrontMatter>(
    TFrontMatter FrontMatter,
    string Body,
    string ContentHash,
    string FrontMatterDigest);
