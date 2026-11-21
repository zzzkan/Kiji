using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiji.Hosting.Tests;

public sealed class StaticAssetRequestHandlerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kiji-asset-handler-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task RangeRequest_ReturnsTheRequestedBytesOfTheLiveFile()
    {
        var source = Path.Combine(_directory, "site.css");
        await File.WriteAllTextAsync(source, "abcdefghij", TestContext.Current.CancellationToken);
        Assert.Equal((206, "cde"), await RequestAsync(source, "bytes=2-4"));

        await File.WriteAllTextAsync(source, "ABCDEFGHIJ", TestContext.Current.CancellationToken);
        Assert.Equal((206, "CDE"), await RequestAsync(source, "bytes=2-4"));
    }

    [Fact]
    public async Task PlainRequest_KeepsTheSdkEndpoint()
    {
        var source = Path.Combine(_directory, "site.css");
        await File.WriteAllTextAsync(source, "abcdefghij", TestContext.Current.CancellationToken);
        Assert.Equal((299, ""), await RequestAsync(source, range: null));
    }

    private static async Task<(int Status, string Body)> RequestAsync(string source, string? range)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = new MemoryStream();
        if (range is not null)
        {
            context.Request.Headers.Range = range;
        }

        var descriptor = new StaticAssetDescriptor
        {
            Route = "site.css",
            AssetPath = "site.css",
            Selectors = [],
            ResponseHeaders = [new StaticAssetResponseHeader("Content-Type", "text/css")],
        };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(descriptor), "site.css"));

        await StaticAssetRequestHandler.HandleAsync(context, static next =>
        {
            next.Response.StatusCode = 299;
            return Task.CompletedTask;
        }, new Dictionary<string, string> { ["site.css"] = source });

        return (context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
}
