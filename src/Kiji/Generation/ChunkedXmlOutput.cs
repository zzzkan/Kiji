using System.Text;
using System.Xml;

namespace Kiji.Generation;

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

    public ValueTask WriteChunkAsync(CancellationToken cancellationToken)
        => _buffer.Length < ChunkSize ? ValueTask.CompletedTask : CopyBufferAsync(cancellationToken);

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
        await _output.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), cancellationToken);
        _buffer.SetLength(0);
    }
}
