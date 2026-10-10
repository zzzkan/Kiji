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

    internal AssetResources(ResourceAssetCollection collection)
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
        _importMap = new(() => ImportMapDefinition.FromResourceCollection(collection));
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
