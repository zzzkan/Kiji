using Microsoft.JSInterop;

namespace Kiji.Rendering;

// Match Blazor static SSR: injection succeeds, but interop fails when called.
internal sealed class UnsupportedJavaScriptRuntime : IJSRuntime
{
    private const string Message = "JavaScript interop calls cannot be issued during static rendering. "
        + "Kiji generates static HTML and cannot execute JavaScript. "
        + "Use a <script> element or JavaScript module for browser-side behavior.";

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => throw new InvalidOperationException(Message);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => throw new InvalidOperationException(Message);

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args)
        => throw new InvalidOperationException(Message);

    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
        => throw new InvalidOperationException(Message);

    public ValueTask<TValue> GetValueAsync<TValue>(string identifier)
        => throw new InvalidOperationException(Message);

    public ValueTask<TValue> GetValueAsync<TValue>(string identifier, CancellationToken cancellationToken)
        => throw new InvalidOperationException(Message);

    public ValueTask SetValueAsync<TValue>(string identifier, TValue value)
        => throw new InvalidOperationException(Message);

    public ValueTask SetValueAsync<TValue>(string identifier, TValue value, CancellationToken cancellationToken)
        => throw new InvalidOperationException(Message);
}
