using System.Text;
using Kiji.Generation;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components;

namespace Kiji.Assets;

/// <summary>The SDK's public URLs and integrity metadata, shared by one site snapshot.</summary>
internal sealed class AssetResources
{
    internal static AssetResources Empty { get; } = new(ResourceAssetCollection.Empty);
    private readonly Lazy<ImportMapDefinition> _importMap;

    /// <param name="collection">The SDK's endpoints, at their public URLs.</param>
    /// <param name="resolveUrl">Maps a site-relative asset path to its public URL; null keeps relative URLs.</param>
    internal AssetResources(ResourceAssetCollection collection, Func<string, string>? resolveUrl = null)
    {
        Collection = collection;
        var identity = new StringBuilder();
        foreach (var resource in collection.OrderBy(static asset => asset.Url, StringComparer.Ordinal))
        {
            BuildFingerprint.AppendPart(identity, resource.Url);
            foreach (var property in resource.Properties ?? [])
            {
                BuildFingerprint.AppendPart(identity, property.Name);
                BuildFingerprint.AppendPart(identity, property.Value);
            }
        }
        Fingerprint = BuildFingerprint.HashText(identity.ToString());
        _importMap = new(() => resolveUrl is null
            ? ImportMapDefinition.FromResourceCollection(collection)
            : CreateImportMap(collection, resolveUrl));
    }

    // The framework prefixes "./" to every URL, which assumes relative URLs under a
    // <base> element. Kiji's URLs are absolute paths, shared by pages at any depth.
    private static ImportMapDefinition CreateImportMap(ResourceAssetCollection collection, Func<string, string> resolveUrl)
    {
        Dictionary<string, string>? imports = null;
        Dictionary<string, string>? integrity = null;
        foreach (var asset in collection)
        {
            if (asset.Properties is null
                || !(asset.Url.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) || asset.Url.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            foreach (var property in asset.Properties)
            {
                if (string.Equals(property.Name, "integrity", StringComparison.OrdinalIgnoreCase))
                {
                    (integrity ??= new(StringComparer.Ordinal))[asset.Url] = property.Value;
                }
                else if (string.Equals(property.Name, "label", StringComparison.OrdinalIgnoreCase))
                {
                    (imports ??= new(StringComparer.Ordinal))[resolveUrl(property.Value)] = asset.Url;
                }
            }
        }
        return new ImportMapDefinition(imports, null, integrity);
    }

    internal ResourceAssetCollection Collection { get; }
    internal string Fingerprint { get; }

    internal ResourceAssetCollection Read()
    {
        PageRenderContext.Current?.Dependencies?.UseAssets(Fingerprint);
        return Collection;
    }

    internal ImportMapDefinition ReadImportMap()
    {
        Read();
        return _importMap.Value;
    }
}
