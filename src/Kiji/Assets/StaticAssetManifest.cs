using System.Text.Json;
using Kiji.Generation;
using Microsoft.AspNetCore.Components;

namespace Kiji.Assets;

internal sealed class StaticAssetManifest
{
    public required StaticAsset[] Assets { get; init; }
    public string[] ContentRoots { get; init; } = [];
    internal string[] DiscoveryRoots { get; init; } = [];
    internal AssetResources Resources { get; init; } = AssetResources.Empty;

    internal static StaticAssetManifest Load(string path, string? endpointsPath = null, Uri? baseUrl = null, bool includeIntegrity = true)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Static asset manifest '{path}' is missing. Build the site with the Razor SDK and Kiji targets before running it.");
        }
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var roots = document.RootElement.GetProperty(nameof(ContentRoots)).Deserialize<string[]>()!;
        if (roots.Any(static root => string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)))
        {
            throw new InvalidOperationException($"Invalid static asset manifest '{path}'. Rebuild the site.");
        }
        var assets = new List<StaticAsset>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveryRoots = new HashSet<string>(StringComparer.Ordinal);
        ReadNode(document.RootElement.GetProperty("Root"), "");
        var resources = new List<ResourceAsset>();
        if (endpointsPath is not null && File.Exists(endpointsPath))
        {
            var sources = assets.ToDictionary(static asset => asset.Target, StringComparer.OrdinalIgnoreCase);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            using var endpointsStream = File.OpenRead(endpointsPath);
            using var endpoints = JsonDocument.Parse(endpointsStream);
            foreach (var endpoint in endpoints.RootElement.GetProperty("Endpoints").EnumerateArray())
            {
                if (endpoint.TryGetProperty("Selectors", out var selectors) && selectors.GetArrayLength() > 0) { continue; }
                var assetFile = endpoint.GetProperty("AssetFile").GetString()!;
                if (!sources.TryGetValue(assetFile, out var source)) { continue; }
                var route = endpoint.GetProperty("Route").GetString()!;
                if (!BuildManifest.IsRelativeOutput(route) || route.Contains('\\', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Invalid static asset endpoint '{route}'.");
                }
                if (paths.Add(route)) { assets.Add(new StaticAsset(source.Source, route)); }
                var properties = endpoint.GetProperty("EndpointProperties").EnumerateArray()
                    .Where(property => includeIntegrity || property.GetProperty("Name").GetString() != "integrity")
                    .Select(static property => new ResourceAssetProperty(property.GetProperty("Name").GetString()!, property.GetProperty("Value").GetString()!)).ToArray();
                var integrity = properties.FirstOrDefault(static property => property.Name == "integrity")?.Value;
                if (integrity is not null && integrity.StartsWith("sha256-", StringComparison.Ordinal))
                {
                    hashes[source.Source] = Convert.ToHexStringLower(Convert.FromBase64String(integrity[7..]));
                }
                var url = baseUrl is null ? route : new Uri(baseUrl, string.Join('/', route.Split('/').Select(Uri.EscapeDataString))).AbsolutePath;
                resources.Add(new ResourceAsset(url, properties));
            }
            for (var index = 0; index < assets.Count; index++)
            {
                if (hashes.TryGetValue(assets[index].Source, out var hash))
                {
                    assets[index] = assets[index] with { ExpectedHash = hash };
                }
            }
        }
        return new StaticAssetManifest
        {
            Assets = [.. assets],
            ContentRoots = roots,
            DiscoveryRoots = [.. discoveryRoots],
            Resources = resources.Count == 0 ? AssetResources.Empty : new AssetResources(new ResourceAssetCollection(resources)),
        };

        void ReadNode(JsonElement node, string target)
        {
            if (node.TryGetProperty("Patterns", out var patterns) && patterns.ValueKind == JsonValueKind.Array)
            {
                foreach (var pattern in patterns.EnumerateArray())
                {
                    discoveryRoots.Add(roots[pattern.GetProperty("ContentRootIndex").GetInt32()]);
                }
            }
            if (node.TryGetProperty("Asset", out var asset) && asset.ValueKind == JsonValueKind.Object)
            {
                if (!BuildManifest.IsRelativeOutput(target) || target.Contains('\\', StringComparison.Ordinal) || !paths.Add(target))
                {
                    throw new InvalidOperationException($"Invalid or duplicate static asset path '{target}'.");
                }
                var source = OutputPathValidator.ResolveUnderRoot(roots[asset.GetProperty("ContentRootIndex").GetInt32()],
                    asset.GetProperty("SubPath").GetString()!, "Static asset source");
                assets.Add(new StaticAsset(source, target));
            }
            if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in children.EnumerateObject())
                {
                    ReadNode(child.Value, target.Length == 0 ? child.Name : target + "/" + child.Name);
                }
            }
        }
    }
}
