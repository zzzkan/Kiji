using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Kiji;
using Kiji.Generation;
using Kiji.SyntheticSite;
using Microsoft.Extensions.Hosting;

var reportPath = Environment.GetEnvironmentVariable("KIJI_PERFORMANCE_REPORT");
if (string.IsNullOrEmpty(reportPath))
{
    await RunSiteAsync(args);
    return;
}
var phases = new List<PhaseTiming>();
BuildPhaseTimer.Observer = (name, elapsed) => phases.Add(new PhaseTiming(name, elapsed.TotalMilliseconds));
var allocated = GC.GetTotalAllocatedBytes(precise: true);
var timer = Stopwatch.StartNew();
try { await RunSiteAsync(args); }
finally { BuildPhaseTimer.Observer = null; }
timer.Stop();
if (phases.Count == 0) { throw new InvalidOperationException("No build phases were reported; BuildPhaseTimer.Observer is not reached."); }
if (!string.IsNullOrEmpty(reportPath))
{
    var report = new
    {
        EntryMilliseconds = timer.Elapsed.TotalMilliseconds,
        AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
        ServerGC = GCSettings.IsServerGC,
        Runtime = Environment.Version.ToString(),
        LogicalProcessors = Environment.ProcessorCount,
        TopLevelPhaseMilliseconds = phases.Where(p => !p.Phase.Contains('.')).Sum(p => p.ElapsedMs),
        Phases = phases
    };
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task RunSiteAsync(string[] args)
{
    var builder = StaticSite.CreateBuilder(args);
    SyntheticSiteDefinition.Configure(builder);
    await builder.Build().RunAsync();
}
