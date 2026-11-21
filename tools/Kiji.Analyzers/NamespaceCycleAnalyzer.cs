using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceCycleAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0007",
        title: "Internal namespaces depend in one direction",
        messageFormat: "'{0}' in {1} uses '{2}' in {3}, but {3} also reaches {1} ({4}). Move the type to the namespace it belongs to, or move what both use to a namespace below them.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Internal types form namespaces whose dependencies have no cycles; public types are the API and are exempt.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var edges = new ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)>();
            start.RegisterSyntaxNodeAction(node => Record(node, edges), SyntaxKind.IdentifierName, SyntaxKind.GenericName);
            start.RegisterCompilationEndAction(end => Report(end, edges));
        });
    }

    private static void Record(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)> edges)
    {
        var source = TopLevel(context.ContainingSymbol as INamedTypeSymbol ?? context.ContainingSymbol?.ContainingType);
        if (source is null || IsApi(source)) { return; }
        var info = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        var target = TopLevel(symbol as INamedTypeSymbol ?? symbol?.ContainingType);
        if (target is null || IsApi(target)
            || !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, context.Compilation.Assembly)) { return; }
        var from = source.ContainingNamespace.ToDisplayString();
        var to = target.ContainingNamespace.ToDisplayString();
        if (from != to) { edges.TryAdd((from, to), (source.Name, target.Name, context.Node.GetLocation())); }
    }

    private static void Report(CompilationAnalysisContext context,
        ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)> edges)
    {
        var graph = edges.Keys.GroupBy(static edge => edge.From)
            .ToDictionary(static group => group.Key, static group => group.Select(static edge => edge.To).ToList());
        foreach (var edge in edges.OrderBy(static edge => edge.Key.From).ThenBy(static edge => edge.Key.To))
        {
            var path = Path(graph, edge.Key.To, edge.Key.From);
            if (path is null) { continue; }
            context.ReportDiagnostic(Diagnostic.Create(Rule, edge.Value.Location,
                edge.Value.Source, edge.Key.From, edge.Value.Target, edge.Key.To, string.Join(" -> ", path)));
        }
    }

    private static List<string>? Path(Dictionary<string, List<string>> graph, string start, string goal)
    {
        var previous = new Dictionary<string, string?> { [start] = null };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == goal)
            {
                var path = new List<string>();
                for (string? step = current; step is not null; step = previous[step]) { path.Insert(0, step); }
                return path;
            }
            if (!graph.TryGetValue(current, out var next)) { continue; }
            foreach (var target in next.Where(target => !previous.ContainsKey(target)))
            {
                previous[target] = current;
                queue.Enqueue(target);
            }
        }
        return null;
    }

    private static INamedTypeSymbol? TopLevel(INamedTypeSymbol? type)
    {
        while (type?.ContainingType is not null) { type = type.ContainingType; }
        return type;
    }

    private static bool IsApi(INamedTypeSymbol type) => type.DeclaredAccessibility == Accessibility.Public;
}
