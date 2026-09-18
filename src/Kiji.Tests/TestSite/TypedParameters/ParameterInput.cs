namespace Kiji.Tests.TestSite.TypedParameters;

public sealed class ParameterInput
{
    private int _reads;
    public int Id { get { _reads++; return 42; } }
    public int GetReadCount() => _reads;
}
