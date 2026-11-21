using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommentAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0004",
        title: "No comments",
        messageFormat: "{0}. Delete it and let names, types, tests or checks say it.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(ReportComments);
        context.RegisterSemanticModelAction(AnalyzeDocumentation);
    }

    private static void ReportComments(SyntaxTreeAnalysisContext context)
    {
        foreach (var trivia in context.Tree.GetRoot(context.CancellationToken).DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, trivia.GetLocation(), "Comments are not allowed"));
            }
        }
    }

    private static void AnalyzeDocumentation(SemanticModelAnalysisContext context)
    {
        var publicApi = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.KijiPublicApi", out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        foreach (var trivia in DocumentationComments(context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken)))
        {
            var owner = trivia.Token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault();
            if (!publicApi || owner is null || !IsExternallyVisible(DeclaredSymbol(context.SemanticModel, owner, context.CancellationToken)))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, trivia.GetLocation(), "XML documentation belongs only to the public API of a library project in src"));
            }
        }
    }

    private static IEnumerable<SyntaxTrivia> DocumentationComments(SyntaxNode root) =>
        root.DescendantTrivia().Where(trivia =>
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

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
