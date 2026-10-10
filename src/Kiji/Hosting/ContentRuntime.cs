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
    internal Generation.DependencyCatalog Dependencies { get; } = new();
    private readonly Dictionary<Type, Action<IServiceCollection>> _registrations = [];
    private readonly ConcurrentDictionary<object, object> _materialized = new();
    private readonly Lock _materializationLock = new();
    private IServiceProvider? _services;

    // A loader may resolve another dictionary (a tag list derived from posts, say),
    // so materialization nests under the reentrant lock. Track dictionary instances
    // within this site; display names need not be unique across types or sites.
    private readonly List<object> _materializing = [];

    /// <summary>
    /// Registers a dictionary for injection. The element type is its identity —
    /// resolving is by type, so a second one of the same type would silently
    /// displace the first rather than coexist with it.
    /// </summary>
    internal void Register<T>(Func<ContentDictionary<T>> createDictionary)
        where T : class
    {
        if (!_registrations.TryAdd(typeof(T), services => services.AddSingleton(createDictionary())))
        {
            throw new InvalidOperationException(
                $"A content dictionary of type '{typeof(T).Name}' is already registered. Each one is identified by its element type, so declare a distinct model type per source.");
        }
    }

    internal void ApplyRegistrations(IServiceCollection services)
    {
        foreach (var registration in _registrations.Values)
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
        lock (_materializationLock)
        {
            _materialized.Clear();
            Dependencies.Invalidate();
        }
    }

    internal TMaterialized GetOrMaterialize<TMaterialized>(object handle, Func<IServiceProvider, TMaterialized> factory)
        where TMaterialized : class
    {
        if (_materialized.TryGetValue(handle, out var existing))
        {
            return (TMaterialized)existing;
        }

        // GetOrAdd alone may invoke the factory multiple times. Serialize first
        // materialization (including nested loaders) and invalidation, so an old
        // in-flight loader cannot repopulate the cache after a content change.
        lock (_materializationLock)
        {
            return Materialize(handle, factory);
        }
    }

    private TMaterialized Materialize<TMaterialized>(object handle, Func<IServiceProvider, TMaterialized> factory)
        where TMaterialized : class
    {
        if (_materialized.TryGetValue(handle, out var existing))
        {
            return (TMaterialized)existing;
        }

        var services = _services
            ?? throw new InvalidOperationException(
                "Content cannot be materialized before the site's services have been initialized.");

        if (_materializing.Contains(handle))
        {
            // Thrown before pushing, so the outer frames' finally blocks unwind the
            // chain as this propagates. Clearing it here would leave them popping an
            // empty list.
            throw new InvalidOperationException(
                $"Content dictionaries form a cycle: {string.Join(" → ", _materializing.Append(handle).Select(DescribeHandle))}. A loader cannot depend, directly or indirectly, on the dictionary it is building.");
        }

        _materializing.Add(handle);
        try
        {
            var materialized = factory(services);
            _materialized[handle] = materialized;
            return materialized;
        }
        finally
        {
            _materializing.RemoveAt(_materializing.Count - 1);
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
