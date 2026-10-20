using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Kiji.Tests.TestSite;

public sealed class JavaScriptRuntimeComponent(IJSRuntime constructorRuntime) : ComponentBase
{
    [Inject] public IJSRuntime JavaScript { get; set; } = default!;
    [Inject] public JavaScriptRuntimeService Service { get; set; } = default!;
    [Parameter] public Action<IJSRuntime, IJSRuntime, IJSRuntime>? ObserveRuntime { get; set; }
    [Parameter] public Func<IJSRuntime, Task>? Invoke { get; set; }

    protected override Task OnInitializedAsync()
    {
        ObserveRuntime?.Invoke(constructorRuntime, JavaScript, Service.Runtime);
        return Invoke?.Invoke(JavaScript) ?? Task.CompletedTask;
    }

    protected override Task OnAfterRenderAsync(bool firstRender)
        => JavaScript.InvokeVoidAsync("afterRender").AsTask();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
        => builder.AddContent(0, "Rendered without JavaScript");
}
