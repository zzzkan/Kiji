using System.Text;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class PooledUtf8TextWriterTests
{
    [Fact]
    public void MixedWrites_ProduceExactUtf8WithoutBom()
    {
        using var writer = new PooledUtf8TextWriter();

        writer.Write("<!doctype html>");
        writer.Write('<');
        writer.Write("html>");
        writer.Write("日本語のテキストと絵文字 🦆 を含む".AsSpan());
        writer.Write(['<', '/', 'h', 't', 'm', 'l', '>'], 0, 7);

        var expected = "<!doctype html><html>日本語のテキストと絵文字 🦆 を含む</html>";
        var bytes = writer.ToArray();
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "Output must not start with a UTF-8 BOM.");
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Write_SurrogatePairSplitAcrossWrites_TranscodesCorrectly()
    {
        using var writer = new PooledUtf8TextWriter();

        var emoji = "🦆";
        writer.Write(emoji[0]);
        writer.Write(emoji[1]);

        Assert.Equal("🦆", Encoding.UTF8.GetString(writer.ToArray()));
    }

    [Fact]
    public void Write_TrailingUnpairedHighSurrogate_FlushesAsReplacementCharacter()
    {
        using var writer = new PooledUtf8TextWriter();

        writer.Write("ok");
        writer.Write('\uD83E');

        Assert.Equal("ok�", Encoding.UTF8.GetString(writer.ToArray()));
    }

    [Fact]
    public void Write_ContentLargerThanInitialCapacity_GrowsAndStaysExact()
    {
        using var writer = new PooledUtf8TextWriter(initialCapacity: 128);

        var chunk = "<p>0123456789 abcdefghijklmnopqrstuvwxyz 日本語テキスト</p>\n";
        var expected = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            writer.Write(chunk);
            expected.Append(chunk);
        }

        Assert.Equal(expected.ToString(), Encoding.UTF8.GetString(writer.ToArray()));
    }

}
