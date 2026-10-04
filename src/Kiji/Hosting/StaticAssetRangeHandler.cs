using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Net.Http.Headers;

namespace Kiji.Hosting;

internal static class StaticAssetRangeHandler
{
    // The SDK's development reload handler currently discards the requested offset
    // and count when sending a file (dotnet/aspnetcore#63320). Keep its ordinary
    // optimized endpoints and delegate ranges to ASP.NET Core's file result instead.
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
        if (context.Request.Headers.Range.Count == 0)
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
