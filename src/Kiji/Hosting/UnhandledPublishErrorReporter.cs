namespace Kiji.Hosting;

/// <summary>
/// Reports an exception that ends a publish as an MSBuild error. Otherwise the terminal logger
/// shows only the exit code, because it hides the runtime's report on standard error.
/// </summary>
internal static class UnhandledPublishErrorReporter
{
    private static int _registered;

    /// <summary>Registers the process-wide handler once. Only Kiji's publish target runs the site this way.</summary>
    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    Console.Error.WriteLine(MSBuildConsoleFormatter.FormatDiagnostic("Kiji", "error", null, exception));
                }
            };
        }
    }
}
