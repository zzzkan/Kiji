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
        description: "A project may declare several namespaces, but no namespace spans the library projects packed into Kiji; the package project adds only its entry point to the root namespace.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!options.TryGetValue("build_property.KijiPackedProjects", out var value)
                || !options.TryGetValue("build_property.MSBuildProjectName", out var project)) { return; }
            var packed = value.Split([','], System.StringSplitOptions.RemoveEmptyEntries).Select(static name => name.Trim()).ToImmutableHashSet();
            var libraries = start.Compilation.References
                .Select(start.Compilation.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>()
                .Where(assembly => packed.Contains(assembly.Name))
                .Select(static assembly => (assembly.Name, Namespaces: Namespaces(assembly.GlobalNamespace)))
                .ToList();
            if (packed.Contains(project))
            {
                start.RegisterSymbolAction(symbol => AnalyzeType(symbol, project, libraries, entryNamespace: null), SymbolKind.NamedType);
            }
            else if (options.TryGetValue("build_property.KijiPackageProject", out var package) && package == project)
            {
                start.RegisterSymbolAction(symbol => AnalyzeType(symbol, project, libraries, entryNamespace: project), SymbolKind.NamedType);
                start.RegisterCompilationEndAction(end => AnalyzeLibraries(end, libraries));
            }
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, string project,
        List<(string Name, ImmutableHashSet<string> Namespaces)> libraries, string? entryNamespace)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.ContainingType is not null || type.IsImplicitlyDeclared || type.ContainingNamespace.IsGlobalNamespace) { return; }
        var name = type.ContainingNamespace.ToDisplayString();
        if (name == entryNamespace) { return; }
        foreach (var library in libraries.Where(library => library.Namespaces.Contains(name)))
        {
            foreach (var location in type.Locations.Where(static location => location.IsInSource))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, project, library.Name, "'" + type.Name + "'"));
            }
        }
    }

    private static void AnalyzeLibraries(CompilationAnalysisContext context, List<(string Name, ImmutableHashSet<string> Namespaces)> libraries)
    {
        for (var first = 0; first < libraries.Count; first++)
        {
            for (var second = first + 1; second < libraries.Count; second++)
            {
                foreach (var name in libraries[first].Namespaces.Intersect(libraries[second].Namespaces).OrderBy(static name => name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Location.None, name, libraries[first].Name, libraries[second].Name, "its types"));
                }
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
