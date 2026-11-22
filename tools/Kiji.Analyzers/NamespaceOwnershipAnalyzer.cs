using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NamespaceOwnershipAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0008",
        title: "A namespace is declared in one project",
        messageFormat: "Namespace {0} is declared in both {1} and {2}. A namespace belongs to one project: rename it in one of them, or move {3} into the other.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A src project may declare several namespaces, but none that a Kiji project it references declares; src/Kiji adds only StaticSite to the root namespace.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.KijiPublicApi", out var publicApi)
                || publicApi != "true") { return; }
            var project = start.Compilation.AssemblyName ?? string.Empty;
            var libraries = start.Compilation.References
                .Select(start.Compilation.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>()
                .Where(static assembly => assembly.Name == "Kiji" || assembly.Name.StartsWith("Kiji.", System.StringComparison.Ordinal))
                .Select(static assembly => (assembly.Name, Namespaces: Namespaces(assembly.GlobalNamespace)))
                .ToList();
            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, project, libraries), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, string project,
        List<(string Name, ImmutableHashSet<string> Namespaces)> libraries)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not null || type.IsImplicitlyDeclared || type.ContainingNamespace.IsGlobalNamespace) { return; }
        var name = type.ContainingNamespace.ToDisplayString();
        if (project == "Kiji" && name == "Kiji" && type.MetadataName == "StaticSite") { return; }
        foreach (var library in libraries.Where(library => library.Namespaces.Contains(name)))
        {
            foreach (var location in type.Locations.Where(static location => location.IsInSource))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, project, library.Name, "'" + type.Name + "'"));
            }
        }
    }

    private static ImmutableHashSet<string> Namespaces(INamespaceSymbol root)
    {
        var names = ImmutableHashSet.CreateBuilder<string>();
        var pending = new Stack<INamespaceSymbol>([root]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!current.IsGlobalNamespace && current.GetTypeMembers().Any(static type => !type.IsImplicitlyDeclared))
            {
                names.Add(current.ToDisplayString());
            }
            foreach (var child in current.GetNamespaceMembers()) { pending.Push(child); }
        }
        return names.ToImmutable();
    }
}
