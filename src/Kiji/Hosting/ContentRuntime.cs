using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Hosting;

/// <summary>
/// Owns the materialized state of all content collections for the current site snapshot.
/// Swapping snapshots (e.g. when content changes during development) simply clears the
/// materialization cache; collections re-materialize lazily on next access.
/// </summary>
internal sealed class ContentRuntime
{
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private readonly ConcurrentDictionary<object, object> _materialized = new();
    private IServiceProvider? _services;

    internal void AddRegistration(Action<IServiceCollection> registration)
    {
        _registrations.Add(registration);
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
                "Content collections cannot be materialized before the app is built. Call KijiBuilder.Build() first.");

        return (TMaterialized)_materialized.GetOrAdd(handle, _ => factory(services));
    }
}
