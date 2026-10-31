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

    /// <summary>Hashes of the content files read in the current snapshot.</summary>
    internal Generation.ContentFileRegistry Files { get; } = new();

    private readonly HashSet<(Type Type, string? Key)> _registeredTypes = [];
    private readonly ConcurrentDictionary<object, object> _materialized = new();
    private readonly Lock _materializationLock = new();
    private IServiceProvider? _services;

    // A loader may resolve another dictionary (a tag list derived from posts, say),
    // so materialization nests under the reentrant lock. Track dictionary instances
    // within this site; display names need not be unique across types or sites.
    private readonly List<object> _materializing = [];

    /// <summary>
    /// Creates a dictionary registration identified by its element type and optional key.
    /// </summary>
    internal ServiceDescriptor Register<T>(Func<ContentDictionary<T>> createDictionary, string? serviceKey = null)
        where T : class
    {
        if (!_registeredTypes.Add((typeof(T), serviceKey)))
        {
            var keyDescription = serviceKey is null ? string.Empty : $" with key '{serviceKey}'";
            throw new InvalidOperationException(
                $"A content dictionary of type '{typeof(T).Name}'{keyDescription} is already registered. Use a distinct key or model type per source.");
        }

        return serviceKey is null
            ? ServiceDescriptor.Singleton(createDictionary())
            : ServiceDescriptor.KeyedSingleton(serviceKey, createDictionary());
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
            Files.Invalidate();
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
