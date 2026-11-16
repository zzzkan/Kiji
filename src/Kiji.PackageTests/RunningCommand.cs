using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Kiji.PackageTests;

// Capture a bounded diagnostic tail, including on startup failure and timeout.
// Only this instance's process tree is ever terminated.
internal sealed class RunningCommand : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<RunningCommand, byte> Running = new();
    internal static Action<string>? Trace { get; set; }
    private readonly Process _process;
    private readonly string _description;
    private readonly StringBuilder _tail = new();
    private readonly Lock _gate = new();
    private readonly Task _drain;
    private readonly TaskCompletionSource<Uri> _address = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RunningCommand(string directory, string[] arguments)
    {
        _description = $"dotnet {string.Join(' ', arguments)} (in {directory})";
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "0";
        start.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "0";
        _process = Process.Start(start)!;
        Running.TryAdd(this, 0);
        Trace?.Invoke($"start pid {_process.Id}: {_description}");
        _drain = Task.WhenAll(ReadAsync(_process.StandardOutput), ReadAsync(_process.StandardError));
    }

    internal string Output { get { lock (_gate) { return _tail.ToString(); } } }

    internal static RunningCommand Start(string directory, params string[] arguments) => new(directory, arguments);

    internal static async Task<string> RunAsync(string directory, params string[] arguments)
    {
        await using var command = new RunningCommand(directory, arguments);
        return await command.CompleteAsync();
    }

    private async Task<string> CompleteAsync()
    {
        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            await DrainAsync();
            if (_process.ExitCode != 0) { throw new InvalidOperationException($"Exit code {_process.ExitCode}."); }
            return Output;
        }
        catch (Exception exception)
        {
            await StopAsync();
            throw new InvalidOperationException($"{_description}\n{Output}", exception);
        }
    }

    internal async Task<Uri> AddressAsync()
    {
        var exited = _process.WaitForExitAsync();
        var ready = await Task.WhenAny(_address.Task, exited).WaitAsync(TimeSpan.FromMinutes(2));
        if (ready == exited)
        {
            await DrainAsync();
            throw new InvalidOperationException($"Server exited before listening: {_description}\n{Output}");
        }
        return await _address.Task;
    }

    internal async Task StopAsync()
    {
        if (!_process.HasExited)
        {
            Trace?.Invoke($"kill tree of pid {_process.Id}");
            _process.Kill(entireProcessTree: true);
            Trace?.Invoke($"killed tree of pid {_process.Id}");
        }
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await DrainAsync();
        if (Running.TryRemove(this, out _)) { Trace?.Invoke($"ended pid {_process.Id} with exit code {_process.ExitCode}"); }
    }

    internal static string DescribeRunning() => string.Join("\n", Running.Keys.Select(command =>
        $"Still running: pid {command._process.Id}: {command._description}\n{command.Output}"));

    internal static void KillRunning()
    {
        foreach (var command in Running.Keys)
        {
            try { command._process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception) { }
        }
    }

    // The pipes close only when every process holding them has exited. A descendant
    // that outlives the tree kill (a build server, or a child started while the tree
    // was being enumerated) inherits them, so waiting for the end of the output would
    // hang the whole test run. Everything the command itself wrote is already read.
    private async Task DrainAsync()
    {
        try { await _drain.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            lock (_gate) { _tail.AppendLine("[A process outliving the command still holds its output.]"); }
            Trace?.Invoke($"output of pid {_process.Id} is still held by another process");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); }
        finally { _process.Dispose(); }
    }

    private async Task ReadAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_gate)
            {
                _tail.AppendLine(line);
                if (_tail.Length > 64 * 1024) { _tail.Remove(0, _tail.Length - 64 * 1024); }
            }
            var match = Regex.Match(line, @"Dev server started at (http://127\.0\.0\.1:\d+)");
            if (match.Success) { _address.TrySetResult(new Uri(match.Groups[1].Value)); }
        }
    }
}
