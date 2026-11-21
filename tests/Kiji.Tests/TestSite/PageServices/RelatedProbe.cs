namespace Kiji.Tests.TestSite.PageServices;

public sealed class RelatedProbe
{
    private int _calls;
    public int Calls => _calls;
    public void Record() => Interlocked.Increment(ref _calls);
}
