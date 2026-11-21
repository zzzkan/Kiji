using Kiji.Generation;
using Xunit;

namespace Kiji.Tests;

public sealed class BuildDependencyRecorderTests
{
    [Fact]
    public void RepeatedReads_PreserveTheDependencyAndRejectConflictingValues()
    {
        var recorder = new BuildDependencyRecorder();
        recorder.AddValue("source#front-matter/POST.MD", "first");
        recorder.AddValue("source#front-matter/POST.MD", new string("first".ToCharArray()));
        var dependency = Assert.Single(recorder.Values);
        Assert.Equal(BuildFingerprint.HashText("first"), dependency.Fingerprint);
        Assert.Throws<IOException>(() => recorder.AddValue("source#front-matter/POST.MD", "second"));
    }
}
