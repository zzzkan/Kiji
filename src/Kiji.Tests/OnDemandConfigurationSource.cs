using Microsoft.Extensions.Configuration;

namespace Kiji.Tests;

internal sealed class OnDemandConfigurationSource(string key, string? value)
    : ConfigurationProvider, IConfigurationSource, IDisposable
{
    public string? Value { get; set; } = value;

    public int DisposeCount { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public override bool TryGet(string requestedKey, out string? result)
    {
        result = Value;
        return string.Equals(key, requestedKey, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => DisposeCount++;
}
