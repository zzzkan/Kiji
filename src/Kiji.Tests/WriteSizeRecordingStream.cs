namespace Kiji.Tests;

/// <summary>A memory stream that records the largest single write.</summary>
internal sealed class WriteSizeRecordingStream : MemoryStream
{
    public int LargestWrite { get; private set; }

    public override void Write(byte[] buffer, int offset, int count)
    {
        LargestWrite = Math.Max(LargestWrite, count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        LargestWrite = Math.Max(LargestWrite, buffer.Length);
        base.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        LargestWrite = Math.Max(LargestWrite, buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        LargestWrite = Math.Max(LargestWrite, count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }
}
