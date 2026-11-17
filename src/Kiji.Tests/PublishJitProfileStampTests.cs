using Xunit;

namespace Kiji.Tests;

public sealed class PublishJitProfileStampTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"kiji-stamp-{Guid.NewGuid():N}");

    public void Dispose() => File.Delete(_path);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stamp_ReadsBackWhatItWrote(bool complete)
    {
        var stamp = new PublishJitProfileStamp("code", 1003, complete);
        File.WriteAllText(_path, stamp.ToString());

        Assert.Equal(stamp, PublishJitProfileStamp.Read(_path));
    }

    [Theory]
    // A stamp that cannot be trusted reads as no stamp, so the profile is recorded again.
    [InlineData("")]
    [InlineData("2 code 10 complete")]
    [InlineData("1 code ten complete")]
    [InlineData("1 code -1 complete")]
    [InlineData("1 code 10 partly")]
    [InlineData("1 code 10")]
    [InlineData("1 code 10 complete extra")]
    public void UnreadableStamp_IsNoStamp(string content)
    {
        File.WriteAllText(_path, content);

        Assert.Null(PublishJitProfileStamp.Read(_path));
    }

    [Fact]
    public void MissingStamp_IsNoStamp()
    {
        Assert.Null(PublishJitProfileStamp.Read(_path));
    }
}
