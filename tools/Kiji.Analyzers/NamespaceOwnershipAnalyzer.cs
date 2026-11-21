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
        messageFormat: "Namespace {0} is declared in both {1} and {2}. Move its types into one project or give the features distinct namespaces.",
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
        context.RegisterCompilationAction(AnalyzePackage);
    }

    private static void AnalyzePackage(CompilationAnalysisContext context)
    {
        if (!context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.KijiPackedProjects", out var value)
            || string.IsNullOrWhiteSpace(value)) { return; }

        var packed = value.Split(',').ToImmutableHashSet(StringComparer.Ordinal);
        var package = context.Compilation.Assembly;
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var assemblies = context.Compilation.References.Select(context.Compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>().Where(assembly => packed.Contains(assembly.Name)).Append(package);
        foreach (var assembly in assemblies)
        {
            var pending = new Stack<INamespaceSymbol>([assembly.GlobalNamespace]);
            while (pending.Count > 0)
            {
                var ns = pending.Pop();
                foreach (var child in ns.GetNamespaceMembers()) { pending.Push(child); }
                if (ns.IsGlobalNamespace) { continue; }

                var name = ns.ToDisplayString();
                var type = ns.GetTypeMembers().FirstOrDefault(type => !type.IsImplicitlyDeclared
                    && !(assembly.Equals(package, SymbolEqualityComparer.Default) && name == package.Name && type.MetadataName == "StaticSite"));
                if (type is null) { continue; }

                if (owners.TryGetValue(name, out var owner))
                {
                    var location = type.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, owner, assembly.Name));
                }
                else
                {
                    owners.Add(name, assembly.Name);
                }
            }
        }
    }
}
