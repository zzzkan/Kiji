using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Tests.TestSite.PageServices;

public sealed class PageServiceArtifact : ISiteArtifact
{
    public string OutputRelativePath => "service.txt";

    public Task WriteAsync(Stream output, SiteOutputContext context, CancellationToken cancellationToken)
    {
        _ = context.Services.GetRequiredService<RenderDependency>();
        return Task.CompletedTask;
    }
}
