using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Hosting;

/// <summary>
/// Owns the materialized state of all content dictionaries for the current site snapshot.
/// Swapping snapshots (e.g. when content changes during development) simply clears the
/// materialization cache; they re-materialize lazily on next access.
/// </summary>
internal sealed class ContentRuntime
{
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private readonly HashSet<Type> _registeredElementTypes = [];
    private readonly ConcurrentDictionary<object, object> _materialized = new();
    private IServiceProvider? _services;

    // A loader may resolve another dictionary (a tag list derived from posts, say),
    // so materialization nests. Tracking the chain turns a cycle into a named error
    // instead of unbounded recursion. Per-thread because a materialization runs to
    // completion on the thread that started it.
    [ThreadStatic]
    private static List<string>? _materializing;

    /// <summary>
    /// Registers a dictionary for injection. The element type is its identity —
    /// resolving is by type, so a second one of the same type would silently
    /// displace the first rather than coexist with it.
    /// </summary>
    internal void Register<T>(ContentDictionary<T> dictionary)
        where T : class
    {
        if (!_registeredElementTypes.Add(typeof(T)))
        {
            throw new InvalidOperationException(
                $"A content dictionary of type '{typeof(T).Name}' is already registered. Each one is identified by its element type, so declare a distinct model type per source.");
        }

        _registrations.Add(services => services.AddSingleton(dictionary));
    }

    internal void ApplyRegistrations(IServiceCollection services)
    {
        foreach (var registration in _registrations)
        {
            registration(services);
        }
    }

    internal void Attach(IServiceProvider services)
    {
        _services = services;
    }

    internal void Invalidate()
    {
        _materialized.Clear();
    }

    internal TMaterialized GetOrMaterialize<TMaterialized>(object handle, Func<IServiceProvider, TMaterialized> factory)
        where TMaterialized : class
    {
        if (_materialized.TryGetValue(handle, out var existing))
        {
            return (TMaterialized)existing;
        }

        var services = _services
            ?? throw new InvalidOperationException(
                "Content cannot be materialized before the app is built. Call KijiBuilder.Build() first.");

        var name = DescribeHandle(handle);
        var chain = _materializing ??= [];
        if (chain.Contains(name, StringComparer.Ordinal))
        {
            // Thrown before pushing, so the outer frames' finally blocks unwind the
            // chain as this propagates. Clearing it here would leave them popping an
            // empty list.
            throw new InvalidOperationException(
                $"Content dictionaries form a cycle: {string.Join(" → ", chain.Append(name))}. A loader cannot depend, directly or indirectly, on the dictionary it is building.");
        }

        chain.Add(name);
        try
        {
            return (TMaterialized)_materialized.GetOrAdd(handle, _ => factory(services));
        }
        finally
        {
            chain.RemoveAt(chain.Count - 1);
        }
    }

    private static string DescribeHandle(object handle)
    {
        var type = handle.GetType();
        return type.IsGenericType
            ? $"ContentDictionary<{type.GetGenericArguments()[0].Name}>"
            : type.Name;
    }
}
