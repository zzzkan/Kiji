namespace Kiji.Tests.TestSite.TypedParameters;

public sealed class FixedParameterLog
{
    private int _count;
    public int Count => _count;
    public void Record() => Interlocked.Increment(ref _count);
}
