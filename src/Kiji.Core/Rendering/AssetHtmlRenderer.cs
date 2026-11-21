using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

internal sealed class AssetHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory, AssetResources resources)
    : StaticHtmlRenderer(services, loggerFactory)
{
    protected override ResourceAssetCollection Assets => resources.Read();
}
