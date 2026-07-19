using BenchmarkDotNet.Attributes;
using Kiji.Components;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kiji.Benchmarks;

/// <summary>
/// Measures the full per-page render (DI scope + HtmlRenderer + render + write) and,
/// separately, just the scope + renderer construction, to quantify how much of the
/// per-page cost is renderer setup (Phase 3-1).
/// </summary>
[MemoryDiagnoser]
public class ComponentRendererBenchmarks
{
    private ComponentRenderer _renderer = default!;
    private ServiceProvider _serviceProvider = default!;
    private Dictionary<string, object?> _rootParameters = default!;
    private Uri _pageUri = default!;

    [GlobalSetup]
    public void Setup()
    {
        var site = new SiteInfo
        {
            BaseUrl = new Uri("https://bench.example.com/"),
            Name = "Kiji Bench",
        };

        _renderer = ComponentRenderer.Create(
            services => services.AddSingleton(site),
            site.BaseUrl);

        var services = new ServiceCollection();
        ComponentRenderer.AddComponentRenderingServices(services);
        services.AddSingleton(site);
        _serviceProvider = services.BuildServiceProvider();

        _rootParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(KijiRoot.PageType)] = typeof(BenchPage),
            [nameof(KijiRoot.PageParameters)] = new Dictionary<string, object?>(StringComparer.Ordinal),
            [nameof(KijiRoot.DefaultLayout)] = null,
        };
        _pageUri = new Uri("https://bench.example.com/bench/");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _renderer.DisposeAsync();
        await _serviceProvider.DisposeAsync();
    }

    [Benchmark]
    public async Task RenderFullPage()
    {
        await _renderer.RenderComponentToAsync<KijiRoot>(TextWriter.Null, _rootParameters, _pageUri);
    }

    [Benchmark]
    public async Task ScopeAndRendererOnly()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
    }
}
