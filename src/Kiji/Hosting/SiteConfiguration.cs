using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Kiji.Hosting;

/// <summary>Keeps existing sections bound to the same immutable values once execution starts.</summary>
internal sealed class SiteConfiguration : IConfigurationManager, IConfigurationRoot, IDisposable
{
    internal const string ReadOnlyMessage = "The site's configuration is read-only once the site starts running.";

    private readonly ConfigurationManager _builder = new();
    private IConfigurationRoot _configuration;
    private readonly HashSet<string> _readKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    internal SiteConfiguration()
    {
        _configuration = _builder;
        Sources = _builder.Sources;
        Properties = ((IConfigurationBuilder)_builder).Properties;
    }

    public string? this[string key]
    {
        get
        {
            if (!_frozen) { _readKeys.Add(key); }
            return _configuration[key];
        }
        set
        {
            EnsureWritable();
            _configuration[key] = value;
        }
    }

    public IList<IConfigurationSource> Sources { get; private set; }

    public IDictionary<string, object> Properties { get; private set; }

    public IEnumerable<IConfigurationProvider> Providers => _configuration.Providers;

    // Sections must retain this root, so sections captured by Configure<T> before
    // startup also read the snapshot rather than a detached live provider.
    public IConfigurationSection GetSection(string key)
    {
        if (!_frozen) { _readKeys.Add(key); }
        return new ConfigurationSection(this, key);
    }

    public IEnumerable<IConfigurationSection> GetChildren()
        => _configuration.GetChildren().Select(section => GetSection(section.Path));

    public IChangeToken GetReloadToken() => _configuration.GetReloadToken();

    public IConfigurationBuilder Add(IConfigurationSource source)
    {
        EnsureWritable();
        ((IConfigurationBuilder)_builder).Add(source);
        return this;
    }

    public IConfigurationRoot Build() => this;

    public void Reload()
    {
        EnsureWritable();
        _configuration.Reload();
    }

    internal void Freeze()
    {
        if (_frozen) { return; }

        var values = _configuration.AsEnumerable().ToDictionary(StringComparer.OrdinalIgnoreCase);
        // On-demand providers may expose a key through TryGet without enumerating it.
        // Include keys read during setup so captured sections and the fingerprint agree.
        foreach (var key in _readKeys)
        {
            if (_configuration.Providers.Any(provider => provider.TryGet(key, out _)))
            {
                values[key] = _configuration[key];
            }
        }
        var snapshot = new ConfigurationRoot([new ReadOnlyConfigurationProvider(values)]);
        Sources = Array.AsReadOnly(Sources.ToArray());
        Properties = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(Properties));
        _configuration = snapshot;
        _frozen = true;
        _readKeys.Clear();
        // Stop file watchers and detach live providers only after the snapshot is complete.
        _builder.Dispose();
    }

    private void EnsureWritable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(ReadOnlyMessage);
        }
    }

    public void Dispose() => ((IDisposable)_configuration).Dispose();
}
