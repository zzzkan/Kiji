namespace Kiji.Tests;

public sealed class FrontMatter
{
    public string? Title { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? Description { get; set; }

    public List<string>? Tags { get; set; }
}
