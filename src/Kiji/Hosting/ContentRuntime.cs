using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
    private readonly ConditionalWeakTable<object, ContentItemIdentity> _items = [];
    private long _generation;
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
            _generation++;
            _materialized.Clear();
            Dependencies.Invalidate();
            Files.Invalidate();
        }
    }

    /// <remarks>Called during materialization, which holds the materialization lock.</remarks>
    internal void RegisterItem(object item, string sourceId, string id, string? digest)
    {
        // Strings are fingerprinted by value, and equal strings may share an instance.
        if (item is string) { return; }

        if (_items.TryGetValue(item, out var existing) && existing.Generation == _generation
            && !(existing.SourceId == sourceId && existing.Id == id))
        {
            // A parameter holding a shared instance cannot name its item: render it every time.
            _items.AddOrUpdate(item, existing with { Shared = true });
            return;
        }

        // Parsed sources are reused across snapshots, so an unchanged file's front matter can
        // return as the same instance and replace its previous registration.
        _items.AddOrUpdate(item, new ContentItemIdentity(sourceId, id, digest, _generation));
    }

    internal ContentItemIdentity? ResolveItem(object item)
    {
        if (!_items.TryGetValue(item, out var identity) || identity.Shared) { return null; }
        if (identity.Generation != Volatile.Read(ref _generation))
        {
            throw new InvalidOperationException(
                $"Content item '{identity.Id}' belongs to a previous content snapshot. Resolve current collection items in the AddPageParameters factory for each build.");
        }
        return identity;
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
