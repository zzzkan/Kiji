using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationPlacementAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0004",
        title: "XML documentation only on public API",
        messageFormat: "XML documentation belongs only to the public API of a packed project. Delete it and let the name, type or a test say what it would.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var packed = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.IsPackable", out var packable)
            && string.Equals(packable, "true", StringComparison.OrdinalIgnoreCase);
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);
        foreach (var trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                continue;
            }

            var owner = trivia.Token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            if (packed && owner is not null && IsExternallyVisible(DeclaredSymbol(context.SemanticModel, owner, context.CancellationToken)))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(trivia.SyntaxTree!, trivia.Span)));
        }
    }

    private static ISymbol? DeclaredSymbol(SemanticModel model, MemberDeclarationSyntax member, CancellationToken cancellationToken) => member switch
    {
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => model.GetDeclaredSymbol(variable, cancellationToken)).FirstOrDefault(),
        _ => model.GetDeclaredSymbol(member, cancellationToken),
    };

    private static bool IsExternallyVisible(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }
}
