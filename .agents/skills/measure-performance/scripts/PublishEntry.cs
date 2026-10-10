using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Kiji.Generation;
using Kiji.SyntheticSite;

// Both packages compile this identical adapter around the frozen site definition.
// Do not force GC or add warmup work to a real publish process.
var reportPath = Environment.GetEnvironmentVariable("KIJI_PERFORMANCE_REPORT");
if (string.IsNullOrEmpty(reportPath))
{
    await BuildRunner.BuildSiteAsync(Directory.GetCurrentDirectory());
    return;
}
var phases = new List<PhaseTiming>();
BuildPhaseTimer.Observer = (name, elapsed) => phases.Add(new PhaseTiming(name, elapsed.TotalMilliseconds));
var allocated = GC.GetTotalAllocatedBytes(precise: true);
var timer = Stopwatch.StartNew();
try { await BuildRunner.BuildSiteAsync(Directory.GetCurrentDirectory()); }
finally { BuildPhaseTimer.Observer = null; }
timer.Stop();
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
