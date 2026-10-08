using System.Text.Json.Nodes;
using Kiji.Assets;
using Kiji.Hosting;
using Kiji.Tests.TestSite;
using Xunit;

namespace Kiji.Tests;

public sealed class StaticAssetWatchFilterTests
{
    [Fact]
    public void DiscoveryPatterns_TrackNewFilesAndDirectoryMovesWithoutWatchingOtherSources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AssetWatch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manifestPath = Path.Combine(root, "assets.json");
            TestSiteAssets.WriteManifest(manifestPath, [], [root], discover: true);
            var json = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            // A nested URL mount still matches paths relative to the physical root.
            var pattern = json["Root"]!["Patterns"]!.DeepClone();
            pattern[0]!["Pattern"] = "**/*.css";
            pattern[0]!["Depth"] = 2;
            json["Root"] = new JsonObject
            {
                ["Children"] = new JsonObject
                {
                    ["_content"] = new JsonObject
                    {
                        ["Children"] = new JsonObject { ["Library"] = new JsonObject { ["Patterns"] = pattern } },
                    },
                },
            };
            File.WriteAllText(manifestPath, json.ToJsonString());
            var initialDirectory = Path.Combine(root, "initial");
            Directory.CreateDirectory(initialDirectory);
            File.WriteAllText(Path.Combine(initialDirectory, "site.css"), "initial");
            var filter = new StaticAssetWatchFilter(root, StaticAssetManifest.Load(manifestPath));
            Directory.Delete(initialDirectory, recursive: true);
            Assert.True(filter.AffectsAsset(initialDirectory));
            Assert.False(filter.AffectsAsset(Path.Combine(root, "Card.razor")));
            Assert.False(filter.AffectsAsset(Path.Combine(root, "ignored.js")));
            Assert.False(filter.AffectsAsset(root + "-other/site.css"));

            var added = Path.Combine(root, "new", "site.css");
            Assert.True(filter.AffectsAsset(added));
            Assert.True(filter.AffectsAsset(Path.GetDirectoryName(added)!));
            var movedDirectory = Path.Combine(root, "moved");
            Directory.CreateDirectory(movedDirectory);
            File.WriteAllText(Path.Combine(movedDirectory, "new.css"), "moved asset");
            Assert.True(filter.AffectsAsset(movedDirectory));
            Directory.Delete(movedDirectory, recursive: true);
            Assert.True(filter.AffectsAsset(movedDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
