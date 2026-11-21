using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceOwnershipAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0008",
        title: "A namespace belongs to one project",
        messageFormat: "Namespace {0} belongs to {1}. Move '{2}' there, or into a namespace that {3} owns.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each src project owns the namespaces under its Namespace in the KijiProject graph, so a namespace never spans assemblies.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue("build_property.KijiNamespaceOwners", out var value)
                || !options.TryGetValue("build_property.MSBuildProjectName", out var project)) { return; }
            var owners = value.Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(static entry => entry.Split('='))
                .Where(static parts => parts.Length == 2)
                .Select(static parts => (Namespace: parts[0].Trim(), Project: parts[1].Trim()))
                .ToImmutableArray();
            if (!owners.Any(owner => owner.Project == project)) { return; }
            start.RegisterSymbolAction(symbol => Analyze(symbol, owners, project), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ImmutableArray<(string Namespace, string Project)> owners, string project)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not null || type.IsImplicitlyDeclared) { return; }
        var name = type.ContainingNamespace.ToDisplayString();
        var matches = owners.Where(owner => name == owner.Namespace || name.StartsWith(owner.Namespace + ".", StringComparison.Ordinal)).ToList();
        if (matches.Count == 0) { return; }
        var longest = matches.Max(static owner => owner.Namespace.Length);
        var owning = matches.Where(owner => owner.Namespace.Length == longest).Select(static owner => owner.Project).ToList();
        if (owning.Contains(project)) { return; }
        var own = string.Join(", ", owners.Where(owner => owner.Project == project).Select(static owner => owner.Namespace));
        foreach (var location in type.Locations.Where(static location => location.IsInSource))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, string.Join(" and ", owning), type.Name, project + " (" + own + ")"));
        }
    }
}
