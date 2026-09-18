namespace Kiji.Tests.TestSite.TypedParameters;

public sealed class ParameterModel(string title) : IDisposable
{
    public string Title { get; set; } = title;
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
    public override string ToString() => "unchanging-model";
}
