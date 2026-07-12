using Kiji.Hosting;

namespace Kiji;

/// <summary>
/// Factory helpers for <see cref="ContentCollection{T}"/>.
/// </summary>
public static class Content
{
    /// <summary>
    /// Creates a standalone collection over a fixed set of items.
    /// Intended for tests and simple scenarios that do not load content from disk.
    /// </summary>
    public static ContentCollection<T> FromItems<T>(IReadOnlyList<T> items)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items);

        var runtime = new ContentRuntime();
        runtime.Attach(EmptyServiceProvider.Instance);
        return new ContentCollection<T>(runtime, _ => items);
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
