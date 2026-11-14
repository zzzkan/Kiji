
namespace Kiji.Tests;

internal static class TestHostBuilderExtensions
{
    internal static TestSiteHost BuildSite(this HostApplicationBuilder builder) => new(builder.Build());
    internal static SiteExecutionPaths GetSitePaths(this HostApplicationBuilder builder) =>
        (SiteExecutionPaths)builder.Services.Single(descriptor => descriptor.ServiceType == typeof(SiteExecutionPaths)).ImplementationInstance!;
    internal static SiteInfo GetSiteInfo(this HostApplicationBuilder builder) =>
        ((StaticSiteOptions)builder.Services.Single(descriptor => descriptor.ServiceType == typeof(StaticSiteOptions)).ImplementationInstance!).SiteInfo!;
}
