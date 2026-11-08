using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kiji.Tests;

/// <summary>Owns a real generic host while rendering tests exercise its shared pipeline repeatedly.</summary>
internal sealed class TestSiteHost(IHost host) : IAsyncDisposable
{
    internal IHost Host { get; } = host;
    internal IServiceProvider ServiceProvider => Host.Services;
    private SiteEngine Engine => ServiceProvider.GetRequiredService<SiteEngine>();
    internal SiteExecutionPaths Paths => ServiceProvider.GetRequiredService<SiteExecutionPaths>();
    internal SiteInfo SiteInfo => Engine.SiteInfo;
    internal SiteSnapshot CreateSnapshot() => Engine.CreateSnapshot();
    internal void InvalidateContent() => Engine.InvalidateContent();
    internal void UseAssetResources(Kiji.Assets.AssetResources resources) => Engine.UseAssetResources(resources);
    internal Task<string> RenderPageAsync(Kiji.Rendering.PageRenderRequest request, CancellationToken token,
        Kiji.Rendering.SharedRenderLifetime? sharedRenders = null) => Engine.RenderPageAsync(request, token, sharedRenders);

    internal Task PublishAsync(string output, CancellationToken token = default)
    {
        var paths = ServiceProvider.GetRequiredService<ResolvedSitePaths>();
        if (Paths.ResolveAgainstRoot(output) != paths.OutputDirectory)
        {
            throw new InvalidOperationException("Choose publish paths when creating the test host.");
        }
        ServiceProvider.GetService<IStartupValidator>()?.Validate();
        return Engine.PublishAsync(token);
    }

    internal Task<(DevServer DevServer, WebApplication WebApplication)> StartDevServerAsync(string[] args,
        CancellationToken token, DevServerStatusReporter? reporter = null)
    {
        ServiceProvider.GetService<IStartupValidator>()?.Validate();
        return Engine.StartDevServerAsync(args, token, reporter);
    }

    public ValueTask DisposeAsync() => Host is IAsyncDisposable disposable ? disposable.DisposeAsync() : DisposeHost();

    private ValueTask DisposeHost()
    {
        Host.Dispose();
        return ValueTask.CompletedTask;
    }
}
