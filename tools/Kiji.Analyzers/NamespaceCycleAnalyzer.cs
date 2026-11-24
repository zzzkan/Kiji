using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceCycleAnalyzer : DiagnosticAnalyzer
{
    public const string LayersProperty = "build_property.KijiNamespaceLayers";

    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0007",
        title: "Internal namespaces depend in one direction",
        messageFormat: "'{0}' in {1} uses '{2}' in {3}, but {3} also reaches {1} ({4}). Move the type to the namespace it belongs to, or move what both use to a namespace below them.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Internal types form namespaces whose dependencies have no cycles; public types are the API and are exempt.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor LayerRule = new(
        id: "KR0010",
        title: "Internal namespaces follow the project's declared layers",
        messageFormat: "{0}",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A project that sets KijiNamespaceLayers lists its internal namespaces from the bottom, separated by commas; an internal type uses internal types only from its own or a lower layer.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule, LayerRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var layers = Layers(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
            var edges = new ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)>();
            var namespaces = new ConcurrentDictionary<string, Location>();
            start.RegisterSyntaxNodeAction(node => Record(node, edges, namespaces), SyntaxKind.IdentifierName, SyntaxKind.GenericName);
            start.RegisterCompilationEndAction(end =>
            {
                if (layers is null)
                {
                    ReportCycles(end, edges);
                }
                else
                {
                    ReportLayers(end, layers, edges, namespaces);
                }
            });
        });
    }

    private static List<string>? Layers(AnalyzerConfigOptions options) =>
        options.TryGetValue(LayersProperty, out var value) && !string.IsNullOrWhiteSpace(value)
            ? [.. value.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(static layer => layer.Trim()).Where(static layer => layer.Length > 0)]
            : null;

    private static void Record(SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)> edges,
        ConcurrentDictionary<string, Location> namespaces)
    {
        var source = TopLevel(context.ContainingSymbol as INamedTypeSymbol ?? context.ContainingSymbol?.ContainingType);
        if (source is null || IsApi(source)) { return; }
        var from = source.ContainingNamespace.ToDisplayString();
        namespaces.TryAdd(from, context.Node.GetLocation());
        var info = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        var target = TopLevel(symbol as INamedTypeSymbol ?? symbol?.ContainingType);
        if (target is null || IsApi(target)
            || !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, context.Compilation.Assembly)) { return; }
        var to = target.ContainingNamespace.ToDisplayString();
        if (from != to) { edges.TryAdd((from, to), (source.Name, target.Name, context.Node.GetLocation())); }
    }

    private static void ReportLayers(CompilationAnalysisContext context, List<string> layers,
        ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)> edges,
        ConcurrentDictionary<string, Location> namespaces)
    {
        foreach (var unlisted in namespaces.Where(ns => !layers.Contains(ns.Key)).OrderBy(static ns => ns.Key, StringComparer.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(LayerRule, unlisted.Value,
                $"{unlisted.Key} holds internal types but is not in KijiNamespaceLayers. Add it to the project's KijiNamespaceLayers at the layer it belongs to"));
        }

        foreach (var edge in edges.OrderBy(static edge => edge.Key.From, StringComparer.Ordinal).ThenBy(static edge => edge.Key.To, StringComparer.Ordinal))
        {
            var from = layers.IndexOf(edge.Key.From);
            var to = layers.IndexOf(edge.Key.To);
            if (from < 0 || to < 0 || to < from) { continue; }
            context.ReportDiagnostic(Diagnostic.Create(LayerRule, edge.Value.Location,
                $"'{edge.Value.Source}' in {edge.Key.From} uses '{edge.Value.Target}' in {edge.Key.To}, which is above it in KijiNamespaceLayers. Move the type to the layer it belongs to, or move what both use below them"));
        }
    }

    private static void ReportCycles(CompilationAnalysisContext context,
        ConcurrentDictionary<(string From, string To), (string Source, string Target, Location Location)> edges)
    {
        var graph = edges.Keys.GroupBy(static edge => edge.From)
            .ToDictionary(static group => group.Key, static group => group.Select(static edge => edge.To).ToList());
        foreach (var edge in edges.OrderBy(static edge => edge.Key.From, StringComparer.Ordinal).ThenBy(static edge => edge.Key.To, StringComparer.Ordinal))
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
