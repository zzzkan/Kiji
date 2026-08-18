using Kiji.Hosting;

namespace Kiji;

/// <summary>
/// Factory helpers for <see cref="ContentDictionary{T}"/>.
/// </summary>
public static class Content
{
    /// <summary>
    /// Creates a standalone dictionary over a fixed set of items, detached from any site.
    /// Intended for tests and simple scenarios that do not load content from disk.
    /// </summary>
    /// <param name="items">The items to expose.</param>
    /// <param name="key">Identifies each item. Keys must be non-empty and unique.</param>
    /// <param name="configure">Declares the dictionary's validation.</param>
    public static ContentDictionary<T> FromItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> key,
        Action<ContentSourceOptions<T>>? configure = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);

        var options = new ContentSourceOptions<T>();
        configure?.Invoke(options);

        var runtime = new ContentRuntime();
        runtime.Attach(EmptyServiceProvider.Instance);
        return new ContentDictionary<T>(runtime, _ => new ContentSourceItems<T>(items, Provenance: null), key, options);
    }

    internal sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
