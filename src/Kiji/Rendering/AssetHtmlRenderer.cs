using Kiji.Assets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

// HtmlRenderer wraps StaticHtmlRenderer but does not expose its Assets property.
// Keep the same static rendering lifecycle and supply the SDK resource collection.
internal sealed class AssetHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory, AssetResources resources)
    : StaticHtmlRenderer(services, loggerFactory)
{
    protected override ResourceAssetCollection Assets => resources.Read();
}
