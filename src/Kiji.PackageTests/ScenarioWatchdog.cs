using System.Diagnostics;
using System.Globalization;

namespace Kiji.PackageTests;

// A hung run shows nothing: the platform reports only skipped and failed tests, and a
// descendant holding the runner's output can outlive the CI step timeout. Record progress
// where CI can follow it, and end the run from inside with the evidence.
internal sealed class ScenarioWatchdog : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(6);
    private readonly Lock _gate = new();
    private readonly string? _tracePath;
    private readonly ManualResetEventSlim _finished = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    internal ScenarioWatchdog(string? tracePath)
    {
        _tracePath = tracePath;
        // A dedicated thread still runs when the pool is starved or a test blocks in a system call.
        new Thread(Watch) { IsBackground = true, Name = "Package scenario watchdog" }.Start();
    }

    internal void Trace(string message)
    {
        if (_tracePath is null) { return; }
        var line = string.Create(CultureInfo.InvariantCulture, $"[{_elapsed.Elapsed.TotalSeconds,6:F1}s] {message}{Environment.NewLine}");
        lock (_gate)
        {
            try { File.AppendAllText(_tracePath, line); }
            catch (IOException) { }
        }
    }

    public void Dispose() => _finished.Set();

    private void Watch()
    {
        if (_finished.Wait(Limit)) { return; }
        var report = $"Package scenarios exceeded {Limit.TotalMinutes} minutes.\n{RunningCommand.DescribeRunning()}\n{ListProcesses()}";
        Trace(report);
        Console.Error.WriteLine(report);
        RunningCommand.KillRunning();
        Trace("Watchdog exits the test process.");
        Environment.Exit(3);
    }

    private static string ListProcesses()
    {
        if (OperatingSystem.IsWindows()) { return ""; }
        try
        {
            using var ps = Process.Start(new ProcessStartInfo("ps", ["-axo", "pid,ppid,stat,etime,command"])
            { RedirectStandardOutput = true, UseShellExecute = false })!;
            var output = ps.StandardOutput.ReadToEndAsync();
            return output.Wait(TimeSpan.FromSeconds(10)) ? output.Result : "ps did not finish.";
        }
        catch (Exception exception) { return $"ps failed: {exception.Message}"; }
    }
}
