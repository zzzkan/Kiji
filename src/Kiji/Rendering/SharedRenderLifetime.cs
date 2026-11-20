namespace Kiji.Rendering;

internal sealed class SharedRenderLifetime : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping;
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _pending = [];
    private bool _disposed;
    private readonly HashSet<string> _outputs = new(StringComparer.Ordinal);

    internal void AddOutput(string path) { lock (_gate) { _outputs.Add(path); } }
    internal string[] Outputs { get { lock (_gate) { return [.. _outputs]; } } }

    internal SharedRenderLifetime(CancellationToken runToken, CancellationToken stoppingToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(runToken, stoppingToken);
        Token = _stopping.Token;
    }

    internal CancellationToken Token { get; }
    internal bool IsStopping => Volatile.Read(ref _disposed) || Token.IsCancellationRequested;

    internal async Task<string> RunAsync(Func<CancellationToken, Task<string>> render)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed) { throw new OperationCanceledException(Token); }
            Token.ThrowIfCancellationRequested();
            _pending.Add(completed.Task);
        }

        try
        {
            var html = await render(Token);
            Token.ThrowIfCancellationRequested();
            return html;
        }
        finally
        {
            lock (_gate)
            {
                completed.SetResult();
                _pending.Remove(completed.Task);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            pending = [.. _pending];
        }

        try
        {
            await _stopping.CancelAsync();
        }
        finally
        {
            await Task.WhenAll(pending);
            _stopping.Dispose();
        }
    }
}
