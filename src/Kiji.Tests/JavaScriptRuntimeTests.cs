using Kiji.Rendering;
using Kiji.Tests.TestSite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Kiji.Tests;

public sealed class JavaScriptRuntimeTests
{
    [Fact]
    public async Task Rendering_InjectsRuntimeIntoComponentsAndPageServices_PerRender()
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        var runtimes = new List<IJSRuntime>();
        var parameters = new Dictionary<string, object?>
        {
            [nameof(JavaScriptRuntimeComponent.ObserveRuntime)] =
                (Action<IJSRuntime, IJSRuntime, IJSRuntime>)((constructor, property, service) =>
                {
                    Assert.Same(constructor, property);
                    Assert.Same(property, service);
                    runtimes.Add(property);
                }),
        };

        for (var render = 0; render < 2; render++)
        {
            var html = await renderer.RenderComponentAsync<JavaScriptRuntimeComponent>(parameters);
            Assert.Equal("Rendered without JavaScript", html);
        }

        Assert.Equal(2, runtimes.Count);
        Assert.NotSame(runtimes[0], runtimes[1]);
    }

    [Theory]
    [InlineData("invoke")]
    [InlineData("constructor")]
    [InlineData("get")]
    [InlineData("set")]
    [InlineData("void")]
    [InlineData("timeout")]
    public async Task Rendering_InteropCallsFailWithStaticRenderingGuidance(string operation)
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        using var output = new StringWriter();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            renderer.RenderComponentToAsync<JavaScriptRuntimeComponent>(output, new Dictionary<string, object?>
            {
                [nameof(JavaScriptRuntimeComponent.Invoke)] = (Func<IJSRuntime, Task>)(async runtime =>
                {
                    switch (operation)
                    {
                        case "invoke": await runtime.InvokeAsync<string>("read", null); break;
                        case "constructor": await runtime.InvokeConstructorAsync("Widget", null); break;
                        case "get": await runtime.GetValueAsync<string>("value"); break;
                        case "set": await runtime.SetValueAsync("value", "test"); break;
                        case "void": await runtime.InvokeVoidAsync("initialize"); break;
                        case "timeout": await runtime.InvokeAsync<string>("read", TimeSpan.FromSeconds(1)); break;
                    }
                }),
            }));

        AssertGuidance(exception);
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rendering_CancellableInteropCallsFailEvenWithCancelledToken(bool cancelled)
    {
        await using var app = CreateApp();
        var renderer = new ComponentRenderer(app.ServiceProvider);
        var token = new CancellationToken(cancelled);

        await renderer.RenderComponentAsync<JavaScriptRuntimeComponent>(new Dictionary<string, object?>
        {
            [nameof(JavaScriptRuntimeComponent.Invoke)] =
                (Func<IJSRuntime, Task>)(async runtime =>
                {
                    AssertGuidance(await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.InvokeAsync<string>("read", token, null)));
                    AssertGuidance(await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.InvokeConstructorAsync("Widget", token, null)));
                    AssertGuidance(await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.GetValueAsync<string>("value", token)));
                    AssertGuidance(await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.SetValueAsync("value", "test", token)));
                    AssertGuidance(await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.InvokeVoidAsync("initialize", token)));
                }),
        });
    }

    private static StaticSite CreateApp()
    {
        var app = TestSiteFactory.Create(new SiteExecutionPaths(AppContext.BaseDirectory));
        app.Info = TestArticleContents.CreateSiteInfo();
        return app.UseServices(services => services.AddScoped<JavaScriptRuntimeService>());
    }

    private static void AssertGuidance(InvalidOperationException exception)
    {
        Assert.Contains("static rendering", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Kiji", exception.Message, StringComparison.Ordinal);
        Assert.Contains("<script>", exception.Message, StringComparison.Ordinal);
    }
}
