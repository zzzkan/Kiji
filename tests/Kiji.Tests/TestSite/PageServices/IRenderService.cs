namespace Kiji.Tests.TestSite.PageServices;

public interface IRenderService
{
    ServiceProbe Probe { get; }
    string Key { get; set; }
}
