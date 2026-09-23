using Kiji.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Benchmarks;

internal static class BenchmarkContentDictionaryFixture
{
    internal static ContentDictionary<T> FromItems<T>(IReadOnlyList<T> items, Func<T, string> key)
        where T : class
    {
        var runtime = new ContentRuntime();
        runtime.Attach(new ServiceCollection().BuildServiceProvider());
        return new ContentDictionary<T>(
            runtime,
            _ => [.. items.Select(item => (key(item), item, Digest: (string?)null))]);
    }
}
