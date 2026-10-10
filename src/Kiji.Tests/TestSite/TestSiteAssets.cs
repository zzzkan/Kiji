using System.Text.Json;
using System.Text.Json.Nodes;
using Kiji.Assets;

namespace Kiji.Tests.TestSite;

internal static class TestSiteAssets
{
    internal static void Bind(StaticSite app, string directory)
    {
        Directory.CreateDirectory(directory);
        var prefix = Path.Combine(app.Paths.RootDirectory, "obj", "test-assets");
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        var assets = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(source => new StaticAsset(source, Path.GetRelativePath(directory, source).Replace('\\', '/'))).ToArray();
        WriteManifest(prefix + ".staticwebassets.publish.runtime.json", assets, [directory], discover: true);
        WriteManifest(prefix + ".staticwebassets.runtime.json", assets, [directory], discover: true);
        app.Paths.AssetManifestBasePath = prefix;
    }

    internal static void WriteManifest(string path, StaticAsset[] assets, string[] roots, bool discover = false)
    {
        roots = [.. roots.Concat(assets.Select(asset => Path.GetDirectoryName(asset.Source)!)).Distinct()
            .Select(root => Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar)];
        var rootNode = new JsonObject();
        foreach (var asset in assets)
        {
            var node = rootNode;
            foreach (var segment in asset.Target.Split('/'))
            {
                var children = (JsonObject)(node["Children"] ??= new JsonObject());
                node = (JsonObject)(children[segment] ??= new JsonObject());
            }
            node["Asset"] = JsonSerializer.SerializeToNode(new
            {
                ContentRootIndex = Array.IndexOf(roots, Path.GetDirectoryName(asset.Source) + Path.DirectorySeparatorChar),
                SubPath = Path.GetFileName(asset.Source),
            });
        }
        if (discover)
        {
            rootNode["Patterns"] = JsonSerializer.SerializeToNode(new[] { new { ContentRootIndex = 0, Pattern = "**", Depth = 0 } });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { ContentRoots = roots, Root = rootNode }));
    }
}
