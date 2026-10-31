using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Hosting;

/// <summary>
/// Owns the materialized state of all content collections for the current site snapshot.
/// Swapping snapshots (e.g. when content changes during development) simply clears the
/// materialization cache; they re-materialize lazily on next access.
/// </summary>
internal sealed class ContentRuntime
{
    internal Generation.DependencyCatalog Dependencies { get; } = new();

    /// <summary>Hashes of the content files read in the current snapshot.</summary>
    internal Generation.ContentFileRegistry Files { get; } = new();

    private readonly HashSet<(Type Type, string? ServiceKey)> _registrations = [];
    private readonly ConcurrentDictionary<object, object> _materialized = new();
    private readonly Lock _materializationLock = new();
    private IServiceProvider? _services;

    /// <summary>
    /// Creates a registration identified by its element type and optional service key.
    /// </summary>
    internal ServiceDescriptor Register<T>(string? serviceKey, Func<ContentCollection<T>> createCollection)
        where T : class
    {
        if (!_registrations.Add((typeof(T), serviceKey)))
        {
            throw new InvalidOperationException(
                $"A content collection of type '{typeof(T).Name}' with service key '{serviceKey ?? "(unkeyed)"}' is already registered. Use AddKeyedMarkdownContent with a different key, or project into a distinct model type per source.");
        }

        var collection = createCollection();
        return serviceKey is null
            ? ServiceDescriptor.Singleton(collection)
            : ServiceDescriptor.KeyedSingleton(serviceKey, collection);
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
        // materialization and invalidation, so an old
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

        var materialized = factory(services);
        _materialized[handle] = materialized;
        return materialized;
    }
}
