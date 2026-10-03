using System.Text.Json;
using Kiji.Generation;

namespace Kiji.Assets;

internal sealed class StaticAssetManifest
{
    public required StaticAsset[] Assets { get; init; }
    public string[] ContentRoots { get; init; } = [];
    internal string[] DiscoveryRoots { get; init; } = [];

    internal static StaticAssetManifest Load(string path)
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
        return new StaticAssetManifest { Assets = [.. assets], ContentRoots = roots, DiscoveryRoots = [.. discoveryRoots] };

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
