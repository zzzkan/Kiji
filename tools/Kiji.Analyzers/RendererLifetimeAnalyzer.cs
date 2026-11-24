using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RendererLifetimeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        id: "KR0011",
        title: "A component renderer lives for one render",
        messageFormat: "'{0}' keeps a {1} beyond one render. Create the renderer where the page renders and dispose it there: a renderer's root components and scopes cannot be reset, so pooling or caching renderers was evaluated and rejected.",
        category: "Repository",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each page gets a fresh Blazor renderer and dispatcher, which isolates the framework's section state.");

    private const string RendererType = "Microsoft.AspNetCore.Components.RenderTree.Renderer";
    private const string HtmlRendererType = "Microsoft.AspNetCore.Components.Web.HtmlRenderer";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var renderers = new[] { start.Compilation.GetTypeByMetadataName(RendererType), start.Compilation.GetTypeByMetadataName(HtmlRendererType) }
                .Where(static type => type is not null).Select(static type => type!).ToImmutableArray();
            if (renderers.IsEmpty)
            {
                return;
            }

            start.RegisterSymbolAction(symbol => Analyze(symbol, renderers), SymbolKind.Field, SymbolKind.Property);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ImmutableArray<INamedTypeSymbol> renderers)
    {
        var type = context.Symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
        var renderer = type is null ? null : FindRenderer(type, renderers);
        if (renderer is not null && !context.Symbol.IsImplicitlyDeclared)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Symbol.Locations[0], context.Symbol.Name, renderer.Name));
        }
    }

    private static ITypeSymbol? FindRenderer(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> renderers)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (renderers.Any(renderer => SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, renderer)))
            {
                return type;
            }
        }

        return type switch
        {
            IArrayTypeSymbol array => FindRenderer(array.ElementType, renderers),
            INamedTypeSymbol named => named.TypeArguments.Select(argument => FindRenderer(argument, renderers)).FirstOrDefault(static found => found is not null),
            _ => null,
        };
    }
}
