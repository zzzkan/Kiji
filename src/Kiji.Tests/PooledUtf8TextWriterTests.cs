using System.Text;
using Kiji.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class PooledUtf8TextWriterTests : IDisposable
{
    private readonly string _testDir;

    public PooledUtf8TextWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"PooledUtf8TextWriterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void WriteToFile_MixedWrites_ProducesExactUtf8WithoutBom()
    {
        var path = Path.Combine(_testDir, "output.html");
        using var writer = new PooledUtf8TextWriter();

        writer.Write("<!doctype html>");
        writer.Write('<');
        writer.Write("html>");
        writer.Write("日本語のテキストと絵文字 🦆 を含む".AsSpan());
        writer.Write(['<', '/', 'h', 't', 'm', 'l', '>'], 0, 7);
        writer.WriteToFile(path);

        var expected = "<!doctype html><html>日本語のテキストと絵文字 🦆 を含む</html>";
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "Output must not start with a UTF-8 BOM.");
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Write_SurrogatePairSplitAcrossWrites_TranscodesCorrectly()
    {
        var path = Path.Combine(_testDir, "surrogate.txt");
        using var writer = new PooledUtf8TextWriter();

        var emoji = "🦆";
        writer.Write(emoji[0]);
        writer.Write(emoji[1]);
        writer.WriteToFile(path);

        Assert.Equal("🦆", File.ReadAllText(path));
    }

    [Fact]
    public void Write_TrailingUnpairedHighSurrogate_FlushesAsReplacementCharacter()
    {
        var path = Path.Combine(_testDir, "unpaired.txt");
        using var writer = new PooledUtf8TextWriter();

        writer.Write("ok");
        writer.Write('\uD83E');
        writer.WriteToFile(path);

        Assert.Equal("ok�", File.ReadAllText(path));
    }

    [Fact]
    public void Write_ContentLargerThanInitialCapacity_GrowsAndStaysExact()
    {
        var path = Path.Combine(_testDir, "large.html");
        using var writer = new PooledUtf8TextWriter(initialCapacity: 128);

        var chunk = "<p>0123456789 abcdefghijklmnopqrstuvwxyz 日本語テキスト</p>\n";
        var expected = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            writer.Write(chunk);
            expected.Append(chunk);
        }

        writer.WriteToFile(path);

        Assert.Equal(expected.ToString(), File.ReadAllText(path));
    }

    [Fact]
    public async Task WriteAsync_ThroughTextWriterBase_Works()
    {
        var path = Path.Combine(_testDir, "async.html");
        using var writer = new PooledUtf8TextWriter();

        await writer.WriteAsync("<html>");
        await writer.WriteLineAsync("body");
        writer.WriteToFile(path);

        Assert.Equal($"<html>body{writer.NewLine}", File.ReadAllText(path));
    }
}
