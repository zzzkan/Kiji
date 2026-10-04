using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Tests.TestSite;

[Route("/assets/{Kind}/")]
public sealed class AssetPage : ComponentBase
{
    [Inject] public ContentDictionary<AssetRenderLog> Logs { get; set; } = default!;
    [Parameter] public string Kind { get; set; } = "assets";
    [Parameter] public string Path { get; set; } = "app.js";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Logs.Values.Single().Counts.AddOrUpdate(Kind, 1, static (_, count) => count + 1);
        if (Kind is "importmap" or "custom")
        {
            builder.OpenComponent<ImportMap>(0);
            if (Kind == "custom")
            {
                builder.AddComponentParameter(1, nameof(ImportMap.ImportMapDefinition),
                    ImportMapDefinition.FromResourceCollection(new ResourceAssetCollection(
                    [new ResourceAsset("/custom/module.js", [new ResourceAssetProperty("label", "app.js")])])));
            }
            builder.CloseComponent();
        }
        else
        {
            builder.OpenElement(2, "a");
            builder.AddAttribute(3, "href", Kind == "plain" ? Path : Assets[Path]);
            builder.AddContent(4, "asset");
            builder.CloseElement();
        }
    }
}
