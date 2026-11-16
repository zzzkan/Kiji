namespace Kiji.Tests;

/// <summary>Holds response transmission after page rendering has completed.</summary>
internal sealed class BlockingResponseStream : MemoryStream
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        await base.WriteAsync(buffer, cancellationToken);
    }
}
