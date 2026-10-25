using System.Runtime.CompilerServices;

namespace Kiji.Hosting;

/// <summary>
/// Reports an exception that ends a publish before the site's logging exists, as when
/// <see cref="StaticSiteBuilder.Build"/> fails, as an MSBuild error. Otherwise the terminal logger
/// shows only the exit code, because it hides the runtime's report on standard error.
/// </summary>
/// <remarks><see cref="StaticSite.RunAsync"/> logs its own failures, which this skips.</remarks>
internal static class UnhandledPublishErrorReporter
{
    private static readonly ConditionalWeakTable<Exception, Exception> LoggedExceptions = [];

    private static int _registered;

    /// <summary>Registers the process-wide handler once. Only Kiji's publish target runs the site this way.</summary>
    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
            {
                if (args.ExceptionObject is Exception exception && !LoggedExceptions.TryGetValue(exception, out var _))
                {
                    Console.Error.WriteLine(MSBuildConsoleFormatter.FormatDiagnostic(
                        BuildLoggerExtensions.CategoryName, "error", null, exception));
                }
            };
        }
    }

    /// <summary>Records that the site's logging has reported the exception.</summary>
    internal static void MarkLogged(Exception exception) => LoggedExceptions.TryAdd(exception, exception);
}
