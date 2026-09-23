using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace Kiji.Rendering;

/// <summary>
/// Transcodes UTF-16 writes into a pooled UTF-8 buffer.
/// </summary>
internal sealed class PooledUtf8TextWriter(int initialCapacity = 64 * 1024) : TextWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    private int _written;
    private char _pendingHighSurrogate;
    private bool _hasPendingHighSurrogate;

    public override Encoding Encoding => Utf8NoBom;

    public override void Write(char value)
    {
        if (!_hasPendingHighSurrogate && value < 0x80)
        {
            if (_written == _buffer.Length)
            {
                Grow(1);
            }

            _buffer[_written++] = (byte)value;
            return;
        }

        WriteSpan([value]);
    }

    public override void Write(string? value)
    {
        WriteSpan(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        WriteSpan(buffer);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        WriteSpan(buffer.AsSpan(index, count));
    }

    /// <summary>
    /// SHA-256 fingerprint of the accumulated UTF-8 bytes; the incremental build
    /// records it as the page's output hash without re-reading the file.
    /// </summary>
    public string GetContentHash()
    {
        FlushPendingSurrogate();
        return Generation.BuildFingerprint.HashBytes(_buffer.AsSpan(0, _written));
    }

    internal byte[] ToArray()
    {
        FlushPendingSurrogate();
        return _buffer.AsSpan(0, _written).ToArray();
    }

    internal bool MatchesFile(string path)
    {
        FlushPendingSurrogate();
        return Generation.BuildFingerprint.FileEquals(path, _buffer.AsSpan(0, _written));
    }

    /// <summary>
    /// Writes the accumulated UTF-8 bytes to <paramref name="path"/> in a single
    /// write, replacing any existing file.
    /// </summary>
    public void WriteToFile(string path)
    {
        FlushPendingSurrogate();

        using var handle = File.OpenHandle(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileOptions.None);
        RandomAccess.Write(handle, _buffer.AsSpan(0, _written), fileOffset: 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
            _written = 0;
        }

        base.Dispose(disposing);
    }

    private void WriteSpan(ReadOnlySpan<char> value)
    {
        if (_hasPendingHighSurrogate)
        {
            if (value.IsEmpty)
            {
                return;
            }

            Span<char> pair = [_pendingHighSurrogate, value[0]];
            _hasPendingHighSurrogate = false;
            TranscodeCore(pair, isFinalBlock: true);
            value = value[1..];
        }

        TranscodeCore(value, isFinalBlock: false);
    }

    private void TranscodeCore(ReadOnlySpan<char> value, bool isFinalBlock)
    {
        while (!value.IsEmpty)
        {
            var status = Utf8.FromUtf16(
                value,
                _buffer.AsSpan(_written),
                out var charsRead,
                out var bytesWritten,
                replaceInvalidSequences: true,
                isFinalBlock);
            _written += bytesWritten;
            value = value[charsRead..];

            switch (status)
            {
                case OperationStatus.Done:
                    return;

                case OperationStatus.DestinationTooSmall:
                    Grow(value.Length * 3 + 4);
                    break;

                case OperationStatus.NeedMoreData:
                    // A high surrogate ended the chunk; hold it for the next write.
                    _pendingHighSurrogate = value[0];
                    _hasPendingHighSurrogate = true;
                    return;

                default:
                    throw new InvalidOperationException($"Unexpected transcoding status '{status}'.");
            }
        }
    }

    private void FlushPendingSurrogate()
    {
        if (!_hasPendingHighSurrogate)
        {
            return;
        }

        // An unpaired trailing high surrogate encodes as U+FFFD, matching
        // replaceInvalidSequences semantics.
        _hasPendingHighSurrogate = false;
        TranscodeCore(['�'], isFinalBlock: true);
    }

    private void Grow(int minimumExtraBytes)
    {
        var newCapacity = Math.Max(_buffer.Length * 2, _written + minimumExtraBytes);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        Array.Copy(_buffer, newBuffer, _written);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
