using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kiji.Rendering;

/// <summary>
/// Renders Razor components to static HTML strings using <see cref="HtmlRenderer"/>.
/// </summary>
public sealed class ComponentRenderer : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Uri? _baseUri;

    private ComponentRenderer(ServiceProvider serviceProvider, Uri? baseUri)
    {
        _serviceProvider = serviceProvider;
        _baseUri = baseUri;
    }

    /// <summary>
    /// Creates a new <see cref="ComponentRenderer"/> with the specified service configuration.
    /// </summary>
    /// <param name="configureServices">Optional action to register services that components may depend on.</param>
    /// <param name="baseUri">Optional site base URI used to initialize navigation for each render.</param>
    public static ComponentRenderer Create(Action<IServiceCollection>? configureServices = null, Uri? baseUri = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_ => HtmlEncoder.Create(UnicodeRanges.All));
        services.AddScoped<StaticNavigationManager>();
        services.AddScoped<NavigationManager>(static provider => provider.GetRequiredService<StaticNavigationManager>());
        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();

        return new ComponentRenderer(serviceProvider, baseUri);
    }

    /// <summary>
    /// Renders the specified component type to its final static HTML document.
    /// </summary>
    public Task<string> RenderComponentAsync<TComponent>(
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
        where TComponent : IComponent
    {
        return RenderComponentAsync(typeof(TComponent), parameters, currentUri);
    }

    /// <summary>
    /// Renders the specified component type to its final static HTML document.
    /// </summary>
    /// <param name="componentType">The component type to render.</param>
    /// <param name="parameters">Optional parameters passed to the component.</param>
    /// <param name="currentUri">Optional absolute URI of the page being rendered; initializes the scoped navigation manager.</param>
    public async Task<string> RenderComponentAsync(
        Type componentType,
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        await using var scope = _serviceProvider.CreateAsyncScope();

        if (currentUri is not null)
        {
            var baseUri = _baseUri ?? new Uri(currentUri.GetLeftPart(UriPartial.Authority) + "/");
            scope.ServiceProvider.GetRequiredService<StaticNavigationManager>().Initialize(baseUri, currentUri);
        }

        await using var renderer = new HtmlRenderer(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

        // HeadOutlet keeps subscriptions on the renderer, so each page needs its own renderer instance
        // to keep head state isolated across renders.
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var document = await renderer.RenderComponentAsync(componentType, CreateParameterView(parameters));
            return document.ToHtmlString();
        });
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
    }

    private static ParameterView CreateParameterView(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return ParameterView.Empty;
        }

        return ParameterView.FromDictionary(
            parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }
}
