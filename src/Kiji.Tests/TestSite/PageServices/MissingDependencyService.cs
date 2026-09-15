namespace Kiji.Tests.TestSite.PageServices;

public sealed class MissingDependencyService(Uri missing)
{
    public Uri Missing { get; } = missing;
}
