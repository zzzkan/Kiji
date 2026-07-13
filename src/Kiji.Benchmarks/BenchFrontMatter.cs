namespace Kiji.Benchmarks;

/// <summary>
/// Representative front matter shape for parser benchmarks.
/// </summary>
public sealed class BenchFrontMatter
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public List<string> Tags { get; set; } = [];
}
