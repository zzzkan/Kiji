using Kiji.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Tests;

internal static class ContentDictionaryFixture
{
    internal static ContentDictionary<T> FromItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> key,
        Action<ContentSourceOptions<T>>? configure = null)
        where T : class
    {
        var options = new ContentSourceOptions<T>();
        configure?.Invoke(options);

        var runtime = new ContentRuntime();
        runtime.Attach(new ServiceCollection().BuildServiceProvider());
        return new ContentDictionary<T>(
            runtime,
            _ => [.. items.Select(static item => (item, SourceFile: (string?)null))],
            key,
            options);
    }
}
