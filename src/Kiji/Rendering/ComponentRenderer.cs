using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

internal sealed class ComponentRenderer(IServiceProvider services, Uri? baseUri)
{
    internal static void AddComponentRenderingServices(IServiceCollection services)
    {
        // Provider-backed logging adds setup cost to every page's HtmlRenderer.
        services.AddLogging();
        services.AddSingleton(_ => HtmlEncoder.Create(UnicodeRanges.All));
        services.AddScoped<StaticNavigationManager>();
        services.AddScoped<NavigationManager>(static provider => provider.GetRequiredService<StaticNavigationManager>());
        services.AddScoped<HeadContentRegistry>();
        services.AddScoped<IComponentActivator, StaticComponentActivator>();
    }

    internal async Task<string> RenderComponentAsync<TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
        where TComponent : IComponent
    {
        using var output = new StringWriter();
        await RenderComponentToAsync<TComponent>(output, parameters, currentUri);
        return output.ToString();
    }

    internal async Task RenderComponentToAsync<TComponent>(
        TextWriter output,
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
        where TComponent : IComponent
    {
        await using var scope = services.CreateAsyncScope();
        if (currentUri is not null)
        {
            scope.ServiceProvider.GetRequiredService<StaticNavigationManager>().Initialize(
                baseUri ?? new Uri(currentUri.GetLeftPart(UriPartial.Authority) + "/"), currentUri);
        }

        // HtmlRenderer captures its scope and cannot reset root component state.
        await using var renderer = new HtmlRenderer(
            scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var document = await renderer.RenderComponentAsync<TComponent>(CreateParameterView(parameters));
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
