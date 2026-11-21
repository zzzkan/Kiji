using Microsoft.JSInterop;

namespace Kiji.Tests.TestSite;

public sealed class JavaScriptRuntimeService(IJSRuntime runtime)
{
    public IJSRuntime Runtime { get; } = runtime;
}
