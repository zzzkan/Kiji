using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// External process/HTTP probe. No production hooks, synthetic sleeps in timings,
// forced GC, browser rendering, or changes to the frozen site definition.
using var config = JsonDocument.Parse(File.ReadAllText(args[0]));
var root = config.RootElement.GetProperty("Root").GetString()!;
var rounds = config.RootElement.GetProperty("Rounds").GetInt32();
var sites = config.RootElement.GetProperty("Sites").EnumerateArray().ToArray();
var rows = new List<Sample>();
var expectedBodies = new Dictionary<string, string>();
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
foreach (var pages in sites.Select(s => s.GetProperty("Pages").GetInt32()).Distinct())
foreach (var mode in new[] { "Direct", "WatchWarm", "WatchFresh" })
for (var round = 0; round <= rounds; round++)
foreach (var variant in round % 2 == 0 ? new[] { "candidate", "baseline" } : new[] { "baseline", "candidate" })
{
    var site = sites.Single(s => s.GetProperty("Pages").GetInt32() == pages && s.GetProperty("Variant").GetString() == variant).GetProperty("Path").GetString()!;
    if (mode == "WatchFresh")
    {
        foreach (var directory in new[] { "bin", "obj", ".kiji" })
        {
            var path = Path.GetFullPath(Path.Combine(site, directory));
            if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Unsafe fixture path."); }
            for (var retry = 0; Directory.Exists(path); retry++)
            {
                try { Directory.Delete(path, true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException && retry < 40)
                {
                    // Outside timing: Windows may finish releasing an exited
                    // child process's image after the parent exit notification.
                    await Task.Delay(50);
                }
            }
        }
        await RunDotnet(site, ["restore"], Path.Combine(root, $"{pages}-{mode}-{round}-{variant}-restore.log"));
    }
    var label = $"{pages}-{mode}-{round}-{variant}";
    var command = mode == "Direct"
        ? new[] { "exec", "bin/Debug/net10.0/Kiji.SyntheticSite.dll" }
        : new[] { "watch", "--project", "Site.csproj", "--non-interactive" };
    var start = StartInfo(site, command);
    var logs = new ConcurrentQueue<string>();
    var applications = new ConcurrentDictionary<int, Process>();
    var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var process = new Process { StartInfo = start };
    var timer = Stopwatch.StartNew();
    var startupMs = 0d;
    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data is null) { return; }
        logs.Enqueue($"{timer.Elapsed.TotalMilliseconds:F3} {e.Data}");
        const string pidPrefix = "Kiji probe PID: ";
        if (e.Data.StartsWith(pidPrefix) && int.TryParse(e.Data[pidPrefix.Length..], out var pid))
        {
            try { applications.TryAdd(pid, Process.GetProcessById(pid)); }
            catch (ArgumentException) { /* Application already exited. */ }
        }
        var match = Regex.Match(e.Data, @"http://127\.0\.0\.1:\d+");
        if (match.Success && !ready.Task.IsCompleted)
        {
            startupMs = timer.Elapsed.TotalMilliseconds;
            ready.TrySetResult(new Uri(match.Value));
        }
    };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { logs.Enqueue($"{timer.Elapsed.TotalMilliseconds:F3} {e.Data}"); } };
    var markdown = Path.Combine(site, "contents/post-00000/index.md");
    var css = Path.Combine(site, "wwwroot/css/app.css");
    var originalMarkdown = File.ReadAllText(markdown);
    var originalCss = File.ReadAllText(css);
    try
    {
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(60));
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        const string article = "/blog/post-00000/";
        var first = await Get(client, article);
        var firstPageReadyMs = timer.Elapsed.TotalMilliseconds;
        EqualBody($"{pages}-{mode}-FirstArticle", first.Body);
        Add("Startup", startupMs);
        Add("FirstPage", first.Elapsed);
        Add("StartToFirstPage", firstPageReadyMs);
        if (mode != "WatchFresh")
        {
            foreach (var (path, metric) in new[] { (article, "Article"), ("/blog/", "Index"), ("/css/app.css", "Static") })
            {
                for (var request = 0; request < 35; request++)
                {
                    var response = await Get(client, path);
                    EqualBody($"{pages}-{mode}-{metric}", response.Body);
                    if (request >= 5) { Add(metric, response.Elapsed); }
                }
            }
            // First edit is warmup. Each subsequent edit starts from the original
            // bytes, and a fresh socket avoids consuming an earlier reload event.
            foreach (var kind in new[] { "Content", "Css" })
            for (var edit = 0; edit < 6; edit++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new UriBuilder(address) { Scheme = "ws", Path = "/_kiji/reload" }.Uri, timeout.Token);
                var marker = $"dev-change-{edit:D2}";
                var editTimer = Stopwatch.StartNew();
                if (kind == "Content") { File.WriteAllText(markdown, originalMarkdown + $"\n\n{marker}\n"); }
                else { File.WriteAllText(css, originalCss + $"/* {marker} */"); }
                var buffer = new byte[64];
                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (Encoding.UTF8.GetString(buffer, 0, received.Count) != "reload") { throw new Exception("Missing reload notification."); }
                var reloadMs = editTimer.Elapsed.TotalMilliseconds;
                var changed = await Get(client, kind == "Content" ? article : "/css/app.css");
                if (!Encoding.UTF8.GetString(changed.Body).Contains(marker, StringComparison.Ordinal)) { throw new Exception("Reload served stale bytes."); }
                if (edit > 0)
                {
                    Add(kind + "Reload", reloadMs);
                    Add(kind + "Visible", editTimer.Elapsed.TotalMilliseconds);
                    Add(kind + "AfterReloadRequest", changed.Elapsed);
                }
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            }
        }
        Console.WriteLine($"{label}: ready {startupMs:F1} ms, first page {first.Elapsed:F1} ms");
    }
    finally
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        await process.WaitForExitAsync();
        process.WaitForExit(); // Drain async stdout/stderr before persisting the log.
        foreach (var application in applications.Values)
        {
            using (application)
            {
                if (!application.HasExited) { application.Kill(entireProcessTree: true); }
                await application.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        File.WriteAllLines(Path.Combine(root, label + ".log"), logs);
        File.WriteAllText(markdown, originalMarkdown);
        File.WriteAllText(css, originalCss);
        File.AppendAllText(Path.Combine(root, "stopped.log"), label + " stopped\n");
        File.WriteAllText(Path.Combine(root, "samples.json"), JsonSerializer.Serialize(rows, jsonOptions));
    }
    void Add(string metric, double ms) => rows.Add(new(pages, mode, round, variant, metric, ms));
}
// Each process is one independent sample. Aggregate within it first; do not treat
// thirty requests from one process as thirty independent startup experiments.
var processRows = rows.Where(r => r.Round > 0).GroupBy(r => new { r.Pages, r.Mode, r.Round, r.Variant, r.Metric })
    .Select(g => new { g.Key.Pages, g.Key.Mode, g.Key.Round, g.Key.Variant, g.Key.Metric, MedianMs = Median(g.Select(r => r.Milliseconds)), P95Ms = Percentile(g.Select(r => r.Milliseconds), .95) }).ToArray();
var summary = processRows.GroupBy(r => new { r.Pages, r.Mode, r.Variant, r.Metric }).Select(g => new
{
    g.Key.Pages, g.Key.Mode, g.Key.Variant, g.Key.Metric,
    MedianMs = Median(g.Select(r => r.MedianMs)), MinMs = g.Min(r => r.MedianMs), MaxMs = g.Max(r => r.MedianMs),
    MedianP95Ms = Median(g.Select(r => r.P95Ms)),
});
File.WriteAllText(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(new { RoundCount = rounds, WarmupProcessesExcluded = 1, ProcessRows = processRows, Summary = summary, BodyHashes = expectedBodies }, jsonOptions));
void EqualBody(string key, byte[] bytes)
{
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    if (expectedBodies.TryGetValue(key, out var expected) && hash != expected) { throw new Exception($"Response bytes differ: {key}"); }
    expectedBodies[key] = hash;
}
static double Median(IEnumerable<double> input)
{
    var values = input.Order().ToArray();
    return (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
}
static double Percentile(IEnumerable<double> input, double percentile)
{
    var values = input.Order().ToArray();
    return values[(int)Math.Ceiling(values.Length * percentile) - 1];
}
static async Task<(byte[] Body, double Elapsed)> Get(HttpClient client, string path)
{
    var timer = Stopwatch.StartNew();
    using var response = await client.GetAsync(path);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadAsByteArrayAsync();
    return (body, timer.Elapsed.TotalMilliseconds);
}
static ProcessStartInfo StartInfo(string site, string[] command)
{
    var info = new ProcessStartInfo("dotnet") { WorkingDirectory = site, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in command) { info.ArgumentList.Add(arg); }
    info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
    info.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";
    info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
    info.Environment.Remove("KIJI_OUTPUT");
    return info;
}
static async Task RunDotnet(string site, string[] command, string log)
{
    using var process = Process.Start(StartInfo(site, command))!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
    finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    File.WriteAllText(log, await output + await errors);
    if (process.ExitCode != 0) { throw new Exception($"dotnet failed: {log}"); }
}
record Sample(int Pages, string Mode, int Round, string Variant, string Metric, double Milliseconds);
