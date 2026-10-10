using System.Text.Json;
using Microsoft.Build.Framework;

namespace Kiji.Performance;

// A measurement-only logger. Compiled into the run's artifacts directory, never
// referenced by Kiji or shipped in its package. Event timestamps include inline
// MSBuild item operations that task-only performance summaries miss.
public sealed class PublishTimingLogger : ILogger
{
    private readonly object gate = new();
    private readonly Dictionary<string, (string Name, string Project, string Target, DateTime Time)> starts = [];
    private readonly List<(string Kind, string Name, string Project, string Target, DateTime Start, DateTime End)> spans = [];
    private int unmatched;
    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;
    public string? Parameters { get; set; }

    public void Initialize(IEventSource source)
    {
        source.TargetStarted += (_, e) => Start("Target", e, e.TargetName, e.ProjectFile, e.TargetName);
        source.TargetFinished += (_, e) => Finish("Target", e);
        source.TaskStarted += (_, e) =>
        {
            string target;
            lock (gate) target = starts.GetValueOrDefault(Key("Target", e)).Name ?? "";
            Start("Task", e, e.TaskName, e.ProjectFile, target);
        };
        source.TaskFinished += (_, e) => Finish("Task", e);
        source.StatusEventRaised += (_, e) =>
        {
            if (e is ProjectEvaluationStartedEventArgs begin)
                Start("Evaluation", begin, "ProjectEvaluation", begin.ProjectFile ?? "", "");
            else if (e is ProjectEvaluationFinishedEventArgs end)
                Finish("Evaluation", end);
        };
    }

    private static string Key(string kind, BuildEventArgs e)
    {
        var c = e.BuildEventContext;
        return c is null ? kind : $"{kind}:{c.NodeId}:{c.ProjectContextId}:" +
            (kind == "Evaluation" ? c.EvaluationId.ToString() : kind == "Target" ? c.TargetId.ToString() : $"{c.TargetId}:{c.TaskId}");
    }

    private void Start(string kind, BuildEventArgs e, string name, string project, string target)
    {
        lock (gate) starts.Add(Key(kind, e), (name, project, target, e.Timestamp.ToUniversalTime()));
    }

    private void Finish(string kind, BuildEventArgs e)
    {
        lock (gate)
        {
            if (starts.Remove(Key(kind, e), out var start))
                spans.Add((kind, start.Name, start.Project, start.Target, start.Time, e.Timestamp.ToUniversalTime()));
            else unmatched++;
        }
    }

    private static (string Category, int Priority) Classify(string kind, string name, string target)
    {
        if (kind == "Evaluation") return ("Evaluation", 10);
        if (kind == "Task")
        {
            if (name.EndsWith("GZipCompress", StringComparison.Ordinal)) return ("Gzip", 100);
            if (name.EndsWith("BrotliCompress", StringComparison.Ordinal)) return ("Brotli", 100);
            if (name == "Csc") return ("Compilation", 100);
            if (name == "Copy") return (target.Contains("Publish", StringComparison.Ordinal) ? "PublishCopy" : "BuildCopy", 90);
            return ("", 0);
        }
        if (name == "KijiGenerateSite") return ("KijiGeneration", 80);
        if (name.StartsWith("_CopyResolvedFilesToPublish", StringComparison.Ordinal)) return ("PublishCopy", 80);
        if (name == "CoreCompile") return ("Compilation", 80);
        if (name.Contains("StaticWebAsset", StringComparison.Ordinal) || name.Contains("ScopedCss", StringComparison.Ordinal)
            || name.Contains("JSModule", StringComparison.Ordinal) || name.StartsWith("Kiji", StringComparison.Ordinal)
                && (name.Contains("Asset", StringComparison.Ordinal) || name == "KijiComputeFilesToPublish"))
            return ("AssetsAndIntegration", 40);
        return ("", 0);
    }

    public void Shutdown()
    {
        lock (gate)
        {
            var classified = spans.Select(s => (Span: s, Class: Classify(s.Kind, s.Name, s.Target)))
                .Where(s => s.Class.Category.Length != 0 && s.Span.End > s.Span.Start).ToArray();
            var boundaries = classified.SelectMany(s => new[] { s.Span.Start, s.Span.End }).Distinct().Order().ToArray();
            var exclusive = new Dictionary<string, double>();
            // Every segment is assigned once. More specific task work takes priority
            // over a surrounding target, so parent and child durations never add up.
            for (var i = 1; i < boundaries.Length; i++)
            {
                var a = boundaries[i - 1];
                var b = boundaries[i];
                var active = classified.Where(s => s.Span.Start <= a && s.Span.End >= b)
                    .OrderByDescending(s => s.Class.Priority).ThenBy(s => s.Span.End - s.Span.Start).ToArray();
                if (active.Length == 0) continue;
                var category = active[0].Class.Category;
                exclusive[category] = exclusive.GetValueOrDefault(category) + (b - a).TotalMilliseconds;
            }
            var report = new
            {
                ExclusiveMilliseconds = exclusive,
                AccountedMilliseconds = exclusive.Values.Sum(),
                UnmatchedEvents = unmatched + starts.Count,
                Tasks = spans.Where(s => s.Kind == "Task").GroupBy(s => (s.Name, s.Target))
                    .Select(g => new { g.Key.Name, g.Key.Target, Count = g.Count(), Milliseconds = g.Sum(s => (s.End - s.Start).TotalMilliseconds) }),
                Targets = spans.Where(s => s.Kind == "Target").GroupBy(s => s.Name)
                    .Select(g => new { Name = g.Key, Count = g.Count(), Milliseconds = g.Sum(s => (s.End - s.Start).TotalMilliseconds) }),
                Spans = spans.Select(s => new { s.Kind, s.Name, s.Project, s.Target, s.Start, s.End,
                    Milliseconds = (s.End - s.Start).TotalMilliseconds })
            };
            File.WriteAllText(Parameters ?? throw new InvalidOperationException("A report path is required."),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
