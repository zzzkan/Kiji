using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0001",
        title: "One type per file",
        messageFormat: "'{0}' is the second type in '{1}'. Move '{0}' to its own file, '{0}.cs', in the same folder.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each file declares one top-level type; nested types stay inside their containing type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var fileName = System.IO.Path.GetFileName(context.Tree.FilePath);
        var seen = 0;
        foreach (var declaration in TopLevelTypes(((CompilationUnitSyntax)root).Members))
        {
            if (seen++ == 0)
            {
                continue;
            }

            var identifier = declaration switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier,
                DelegateDeclarationSyntax @delegate => @delegate.Identifier,
                _ => default,
            };
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.ValueText, fileName));
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> TopLevelTypes(SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    foreach (var type in TopLevelTypes(ns.Members))
                    {
                        yield return type;
                    }

                    break;
                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                    yield return member;
                    break;
            }
        }
    }
}
