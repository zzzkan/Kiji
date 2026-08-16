using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Kiji.Hosting;

/// <summary>
/// Mounts the dev and preview servers under the site's base path, so what you browse
/// locally matches what the deployed site serves.
/// </summary>
internal static class SiteBasePathExtensions
{
    /// <summary>
    /// Serves the application under <paramref name="basePath"/>. Requests outside the
    /// prefix are rejected rather than passed through: production serves nothing there,
    /// and letting them succeed locally would hide a forgotten
    /// <see cref="SiteInfo.Path(string)"/> until after deployment. Does nothing when the
    /// site is published at the domain root.
    /// </summary>
    internal static void UseSiteBasePath(this WebApplication web, string basePath)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(basePath);

        var prefix = new PathString(basePath.TrimEnd('/'));
        if (!prefix.HasValue)
        {
            return;
        }

        web.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(prefix))
            {
                await next(context);
                return;
            }

            // The site root is the friendly landing spot; send it to the prefix rather
            // than a 404. Temporary, so a cached redirect never outlives a BaseUrl change.
            if (!context.Request.Path.HasValue || context.Request.Path == "/")
            {
                context.Response.Redirect(prefix.Value + "/" + context.Request.QueryString);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                $"404. This site is served under {prefix}/ because SiteInfo.BaseUrl has that path. "
                + "Use Site.Path(\"...\") for site-root-relative URLs so they resolve here and once deployed.",
                context.RequestAborted);
        });

        web.UsePathBase(prefix);
    }
}
