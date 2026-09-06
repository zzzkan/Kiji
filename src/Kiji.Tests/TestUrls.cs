namespace Kiji.Tests;

/// <summary>
/// Command-line arguments for servers started by tests.
/// </summary>
internal static class TestUrls
{
    /// <summary>
    /// Binds the dev server to a port the OS picks. Passed as arguments rather than set as
    /// an environment variable because test classes run in parallel and share the process.
    /// </summary>
    internal static string[] EphemeralPort { get; } = ["--urls", "http://127.0.0.1:0"];
}
