using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Kiji.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Benchmarks;

/// <summary>
/// Compares the previous unvalidated provider with the validated page-service provider
/// over identical registrations and HTML. This isolates DI validation from content I/O.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PageServiceBenchmarks
{
    private ServiceCollection _registrations = null!;
    private ServiceProvider _baseline = null!;
    private ServiceProvider _validated = null!;
    private ComponentRenderer _baselineRenderer = null!;
    private ComponentRenderer _validatedRenderer = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _registrations = new ServiceCollection();
        ComponentRenderer.AddComponentRenderingServices(_registrations);
        _registrations.AddSingleton(BenchmarkContentDictionaryFixture.FromItems<PageServiceBenchmarkItem>(
            [new("page", "Page service benchmark")], item => item.Key));
        _registrations.AddScoped<PageServiceBenchmarkHelper>();
        _baseline = CreateProvider(validate: false);
        _validated = CreateProvider(validate: true);
        _baselineRenderer = ComponentRenderer.Attach(_baseline, baseUri: null);
        _validatedRenderer = ComponentRenderer.Attach(_validated, baseUri: null);
        var before = await _baselineRenderer.RenderComponentAsync<PageServiceBenchmarkPage>();
        var after = await _validatedRenderer.RenderComponentAsync<PageServiceBenchmarkPage>();
        if (before != "<h1>Page service benchmark</h1>" || before != after)
        {
            throw new InvalidOperationException("Provider variants must produce identical HTML.");
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Render")]
    public Task RenderBefore() => _baselineRenderer.RenderComponentToAsync<PageServiceBenchmarkPage>(TextWriter.Null);

    [Benchmark, BenchmarkCategory("Render")]
    public Task RenderAfter() => _validatedRenderer.RenderComponentToAsync<PageServiceBenchmarkPage>(TextWriter.Null);

    [Benchmark(Baseline = true), BenchmarkCategory("Provider")]
    public async Task BuildBefore()
    {
        await using var provider = CreateProvider(validate: false);
    }

    [Benchmark, BenchmarkCategory("Provider")]
    public async Task BuildAfter()
    {
        await using var provider = CreateProvider(validate: true);
    }

    private ServiceProvider CreateProvider(bool validate) => _registrations.BuildServiceProvider(
        new ServiceProviderOptions { ValidateScopes = validate, ValidateOnBuild = validate });

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _baselineRenderer.DisposeAsync();
        await _validatedRenderer.DisposeAsync();
        await _baseline.DisposeAsync();
        await _validated.DisposeAsync();
    }
}
