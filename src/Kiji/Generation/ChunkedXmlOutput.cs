using System.Text;
using System.Xml;

namespace Kiji.Generation;

/// <summary>
/// A synchronous <see cref="XmlWriter"/> whose bytes reach the output in chunks. An
/// asynchronous writer awaits every element, which costs more than the document's I/O;
/// chunks keep memory bounded, so the output's own buffering still applies.
/// </summary>
internal sealed class ChunkedXmlOutput : IDisposable
{
    private const int ChunkSize = 64 * 1024;

    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    private readonly MemoryStream _buffer = new();
    private readonly Stream _output;

    public ChunkedXmlOutput(Stream output)
    {
        _output = output;
        Writer = XmlWriter.Create(_buffer, Settings);
    }

    public XmlWriter Writer { get; }

    /// <summary>Copies the buffered bytes to the output once a chunk has accumulated.</summary>
    public ValueTask WriteChunkAsync(CancellationToken cancellationToken)
        => _buffer.Length < ChunkSize ? ValueTask.CompletedTask : CopyBufferAsync(cancellationToken);

    /// <summary>Ends the document and copies the remaining bytes to the output.</summary>
    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        Writer.WriteEndDocument();
        Writer.Flush();
        return CopyBufferAsync(cancellationToken);
    }

    public void Dispose()
    {
        Writer.Dispose();
        _buffer.Dispose();
    }

    private async ValueTask CopyBufferAsync(CancellationToken cancellationToken)
    {
        // The writer only appends, so the buffer can restart once its bytes are copied.
        await _output.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), cancellationToken);
        _buffer.SetLength(0);
    }
}
