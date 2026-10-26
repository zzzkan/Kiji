using Microsoft.Extensions.Configuration;

namespace Kiji.Hosting;

/// <summary>Preserves all effective keys, including explicit nulls, without live sources.</summary>
internal sealed class ReadOnlyConfigurationProvider : ConfigurationProvider
{
    internal ReadOnlyConfigurationProvider(IEnumerable<KeyValuePair<string, string?>> values)
    {
        Data = values.ToDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public override void Set(string key, string? value)
        => throw new InvalidOperationException(SiteConfiguration.ReadOnlyMessage);
}
