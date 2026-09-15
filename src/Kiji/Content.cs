using Kiji.Hosting;

namespace Kiji;

/// <summary>
/// Factory helpers for <see cref="ContentDictionary{T}"/>.
/// </summary>
public static class Content
{
    /// <summary>Creates a standalone content dictionary from fixed items.</summary>
    /// <param name="key">Returns a non-empty key unique within the source, compared case-insensitively.</param>
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
