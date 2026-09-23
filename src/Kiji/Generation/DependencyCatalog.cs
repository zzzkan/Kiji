using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>Resolves dependencies against the current snapshot, never against cached data.</summary>
internal sealed class DependencyCatalog
{
    private readonly ConcurrentDictionary<string, Lazy<string?>> _snapshot = new(StringComparer.Ordinal);
    internal void Invalidate() => _snapshot.Clear();
    private readonly ConcurrentDictionary<string, Func<string?>> _resolvers = new(StringComparer.Ordinal);
    internal void Register(string key, Func<string?> resolve)
    {
        if (!_resolvers.TryAdd(key, resolve)) { throw new InvalidOperationException($"Dependency source '{key}' is already registered."); }
    }
    private readonly Dictionary<string, Func<string, string?>> _sources = new(StringComparer.Ordinal);
    internal void RegisterSource(string key, Func<string, string?> resolver) => _sources.Add(key, resolver);
    internal string? Resolve(string key) => _snapshot.GetOrAdd(key,
        name => new Lazy<string?>(() => ResolveCurrent(name), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private string? ResolveCurrent(string key)
    {
        if (_resolvers.TryGetValue(key, out var resolve)) { return resolve(); }
        foreach (var source in _sources)
        {
            if (key.StartsWith(source.Key + "/", StringComparison.Ordinal))
            {
                return source.Value(key[(source.Key.Length + 1)..]);
            }
        }
        return null;
    }
}
