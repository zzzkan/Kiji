namespace Kiji.Benchmarks;

public sealed class BenchFrontMatter
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public List<string> Tags { get; set; } = [];
}
