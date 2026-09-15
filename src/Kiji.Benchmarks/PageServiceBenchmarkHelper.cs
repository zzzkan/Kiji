namespace Kiji.Benchmarks;

public sealed class PageServiceBenchmarkHelper(ContentDictionary<PageServiceBenchmarkItem> items)
{
    public string Title => items["page"].Title;
}
