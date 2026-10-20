using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Kiji.Rendering;

internal sealed class ComponentRenderer(IServiceProvider services)
{
    internal static void AddComponentRenderingServices(IServiceCollection services)
    {
        // Provider-backed logging adds setup cost to every page's HtmlRenderer.
        services.AddLogging();
        services.AddSingleton(_ => HtmlEncoder.Create(UnicodeRanges.All));
        services.AddScoped(static provider =>
        {
            var context = PageRenderContext.Current ?? throw new InvalidOperationException(
                "PageInfo is only available during page rendering.");
            var site = provider.GetRequiredService<SiteInfo>();
            return new PageInfo(site.ResolveUrl(context.RoutePath));
        });
        services.AddScoped<HeadContentRegistry>();
        services.AddScoped<NavigationManager, StaticNavigationManager>();
        services.AddScoped<IComponentActivator, StaticComponentActivator>();
        services.AddScoped<IJSRuntime, UnsupportedJavaScriptRuntime>();
    }

    internal async Task<string> RenderComponentAsync<TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        using var output = new StringWriter();
        await RenderComponentToAsync<TComponent>(output, parameters);
        return output.ToString();
    }

    internal async Task RenderComponentToAsync<TComponent>(
        TextWriter output,
        IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        await using var scope = services.CreateAsyncScope();
        // HtmlRenderer captures its scope and cannot reset root component state.
        await using var renderer = new AssetHtmlRenderer(
            scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>(),
            PageRenderContext.Current?.Assets ?? Assets.AssetResources.Empty);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var document = renderer.BeginRenderingComponent(typeof(TComponent), CreateParameterView(parameters));
            await document.QuiescenceTask;
            document.WriteHtmlTo(output);
        });
    }

    private static ParameterView CreateParameterView(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return ParameterView.Empty;
        }

        // ParameterView reads the dictionary only during rendering.
        return ParameterView.FromDictionary(parameters as IDictionary<string, object?>
            ?? parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }
}
