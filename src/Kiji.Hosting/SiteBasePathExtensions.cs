using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Kiji.Hosting;

internal static class SiteBasePathExtensions
{
    internal static void UseSiteBasePath(this WebApplication web, string basePath)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(basePath);

        var prefix = PathString.FromUriComponent(basePath.TrimEnd('/'));
        if (!prefix.HasValue)
        {
            return;
        }

        web.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(prefix))
            {
                await Results.LocalRedirect(prefix.ToUriComponent() + "/" + context.Request.QueryString)
                    .ExecuteAsync(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments(prefix))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                $"404. This site is served under {prefix}/ because SiteInfo.BaseUrl has that path. "
                + "Build page links with Site.ResolveUrl and asset links with Assets so they resolve here and once deployed.",
                context.RequestAborted);
        });

        web.UsePathBase(prefix);
    }
}
