using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Net.Http.Headers;

namespace Kiji.Hosting;

internal static class StaticAssetRequestHandler
{
    // SDK development endpoints evaluate conditions against build-time metadata,
    // before patching the response with live bytes, and discard range offsets
    // (dotnet/aspnetcore#63320). Use a live file result for these requests; ordinary
    // requests retain the SDK's optimized endpoints and compression.
    internal static async Task HandleAsync(HttpContext context, RequestDelegate next, IReadOnlyDictionary<string, string> files)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<StaticAssetDescriptor>() is not { } asset
            || !files.TryGetValue(asset.Route, out var source))
        {
            await next(context);
            return;
        }

        var file = new FileInfo(source);
        if (!file.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var headers = context.Request.Headers;
        if (headers.Range.Count == 0 && headers.IfMatch.Count == 0 && headers.IfNoneMatch.Count == 0
            && headers.IfModifiedSince.Count == 0 && headers.IfUnmodifiedSince.Count == 0)
        {
            await next(context);
            return;
        }

        await using var stream = file.OpenRead();
        var hash = await SHA256.HashDataAsync(stream, context.RequestAborted);
        stream.Position = 0;
        var contentType = asset.ResponseHeaders.FirstOrDefault(static header => header.Name == "Content-Type")?.Value;
        context.Response.Headers.CacheControl = "no-store";
        await Results.Stream(stream, contentType, lastModified: file.LastWriteTimeUtc,
                entityTag: new EntityTagHeaderValue('"' + Convert.ToBase64String(hash) + '"'), enableRangeProcessing: true)
            .ExecuteAsync(context);
    }
}
