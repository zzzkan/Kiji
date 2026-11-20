using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Kiji.Hosting;

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
