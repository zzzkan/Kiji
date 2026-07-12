using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
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
    private readonly bool _ownsProvider;

    private ComponentRenderer(ServiceProvider serviceProvider, Uri? baseUri, bool ownsProvider)
    {
        _serviceProvider = serviceProvider;
        _baseUri = baseUri;
        _ownsProvider = ownsProvider;
    }

    /// <summary>
    /// Creates a new <see cref="ComponentRenderer"/> with the specified service configuration.
    /// </summary>
    /// <param name="configureServices">Optional action to register services that components may depend on.</param>
    /// <param name="baseUri">Optional site base URI used to initialize navigation for each render.</param>
    public static ComponentRenderer Create(Action<IServiceCollection>? configureServices = null, Uri? baseUri = null)
    {
        var services = new ServiceCollection();
        AddComponentRenderingServices(services);
        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();

        return new ComponentRenderer(serviceProvider, baseUri, ownsProvider: true);
    }

    /// <summary>
    /// Wraps an existing provider without taking ownership of it. The provider must
    /// contain the registrations added by <see cref="AddComponentRenderingServices"/>.
    /// </summary>
    internal static ComponentRenderer Attach(ServiceProvider serviceProvider, Uri? baseUri)
    {
        return new ComponentRenderer(serviceProvider, baseUri, ownsProvider: false);
    }

    /// <summary>
    /// Registers the services component rendering depends on: logging, HTML encoding,
    /// and the per-render scoped navigation manager.
    /// </summary>
    internal static void AddComponentRenderingServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_ => HtmlEncoder.Create(UnicodeRanges.All));
        services.AddScoped<StaticNavigationManager>();
        services.AddScoped<NavigationManager>(static provider => provider.GetRequiredService<StaticNavigationManager>());
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
        string? html = null;
        await RenderComponentCoreAsync(componentType, parameters, currentUri, document => html = document.ToHtmlString());
        return html!;
    }

    /// <summary>
    /// Renders the specified component type directly to a writer, avoiding an
    /// intermediate full-page string.
    /// </summary>
    /// <param name="output">The destination writer; owned by the caller.</param>
    /// <param name="parameters">Optional parameters passed to the component.</param>
    /// <param name="currentUri">Optional absolute URI of the page being rendered; initializes the scoped navigation manager.</param>
    public Task RenderComponentToAsync<TComponent>(
        TextWriter output,
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
        where TComponent : IComponent
    {
        return RenderComponentToAsync(typeof(TComponent), output, parameters, currentUri);
    }

    /// <summary>
    /// Renders the specified component type directly to a writer, avoiding an
    /// intermediate full-page string.
    /// </summary>
    /// <param name="componentType">The component type to render.</param>
    /// <param name="output">The destination writer; owned by the caller.</param>
    /// <param name="parameters">Optional parameters passed to the component.</param>
    /// <param name="currentUri">Optional absolute URI of the page being rendered; initializes the scoped navigation manager.</param>
    public Task RenderComponentToAsync(
        Type componentType,
        TextWriter output,
        IReadOnlyDictionary<string, object?>? parameters = null,
        Uri? currentUri = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        return RenderComponentCoreAsync(componentType, parameters, currentUri, document => document.WriteHtmlTo(output));
    }

    private async Task RenderComponentCoreAsync(
        Type componentType,
        IReadOnlyDictionary<string, object?>? parameters,
        Uri? currentUri,
        Action<HtmlRootComponent> writeDocument)
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
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var document = await renderer.RenderComponentAsync(componentType, CreateParameterView(parameters));
            writeDocument(document);
        });
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsProvider)
        {
            await _serviceProvider.DisposeAsync();
        }
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
