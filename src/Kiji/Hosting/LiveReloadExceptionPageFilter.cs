using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Kiji.Hosting;

/// <summary>
/// Adds the live reload client to the developer exception page, so the page reloads once
/// a content or code change fixes the error.
/// </summary>
internal sealed class LiveReloadExceptionPageFilter(PathString pathBase) : IDeveloperPageExceptionFilter
{
    public async Task HandleExceptionAsync(ErrorContext errorContext, Func<ErrorContext, Task> next)
    {
        var response = errorContext.HttpContext.Response;
        var body = response.Body;
        using var page = new MemoryStream();
        response.Body = page;
        try
        {
            await next(errorContext);
        }
        finally
        {
            response.Body = body;
        }

        // Requests that do not accept HTML get a plain-text page.
        if (response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            page.Position = 0;
            await page.CopyToAsync(body, errorContext.HttpContext.RequestAborted);
            return;
        }

        var html = DevServer.InjectLiveReloadScript(System.Text.Encoding.UTF8.GetString(page.GetBuffer(), 0, (int)page.Length), pathBase);
        response.ContentLength = null;
        await response.WriteAsync(html, errorContext.HttpContext.RequestAborted);
    }
}
