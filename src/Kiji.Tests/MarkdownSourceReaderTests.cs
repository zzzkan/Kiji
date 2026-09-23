using System.Text;
using Kiji.Markdown;
using Xunit;

namespace Kiji.Tests;

public sealed class MarkdownSourceReaderTests
{
    [Fact]
    public void DecodeText_PreservesStreamReaderEncodingAndFallback()
    {
        const string content = "---\ntitle: 日本語 🐦\n---\n\n本文\r\n\uFEFF";
        Encoding[] encodings = [new UTF8Encoding(false), new UTF8Encoding(true),
            Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, true)];
        foreach (var encoding in encodings)
        {
            byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(content)];
            Assert.Equal(content, MarkdownSourceReader.DecodeText(bytes));
            // Truncated preambles and partial multibyte characters must retain the
            // old replacement behavior, including an empty or BOM-only input.
            for (var length = 0; length <= bytes.Length; length++)
            {
                AssertSameDecoding(bytes[..length]);
            }
        }

        byte[][] malformed = [[0xFF], [0xEF, 0xBB], [0xEF, 0xBB, 0xBF, 0xFF],
            [0xED, 0xA0, 0x80], [0xF0, 0x80, 0x80, 0xAF], [0xE2, 0x28, 0xA1]];
        foreach (var bytes in malformed) { AssertSameDecoding(bytes); }

        static void AssertSameDecoding(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            Assert.Equal(reader.ReadToEnd(), MarkdownSourceReader.DecodeText(bytes));
        }
    }
}
