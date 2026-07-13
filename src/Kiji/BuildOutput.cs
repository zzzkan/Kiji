namespace Kiji;

/// <summary>
/// Console output for build progress. Per-item detail lines are suppressed unless
/// verbose mode is enabled (<c>--verbose</c>): the console is a global lock plus
/// synchronous I/O, and writing one line per page from the parallel render loop
/// can dominate large builds.
/// </summary>
internal static class BuildOutput
{
    internal static bool Verbose { get; set; }

    internal static void Info(string message)
    {
        Console.WriteLine(message);
    }

    internal static void Detail(string message)
    {
        if (Verbose)
        {
            Console.WriteLine(message);
        }
    }
}
