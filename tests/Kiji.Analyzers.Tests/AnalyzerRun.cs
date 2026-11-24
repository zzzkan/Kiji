using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Kiji.Analyzers.Tests;

internal static class AnalyzerRun
{
    private static readonly ImmutableArray<MetadataReference> Framework =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    public static CSharpCompilation Compile(string assemblyName, IEnumerable<(string Path, string Source)> files, IEnumerable<MetadataReference>? references = null)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            files.Select(file => CSharpSyntaxTree.ParseText(file.Source, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose), file.Path)),
            Framework.AddRange(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return compilation;
    }

    public static async Task<IReadOnlyList<Diagnostic>> DiagnosticsAsync(DiagnosticAnalyzer analyzer, CSharpCompilation compilation, IReadOnlyDictionary<string, string>? buildProperties = null)
    {
        var options = new AnalyzerOptions([], new BuildPropertiesProvider(buildProperties ?? new Dictionary<string, string>()));
        var diagnostics = await compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    public static Task<IReadOnlyList<Diagnostic>> DiagnosticsAsync(DiagnosticAnalyzer analyzer, string source, IReadOnlyDictionary<string, string>? buildProperties = null) =>
        DiagnosticsAsync(analyzer, Compile("Sample", [("Sample.cs", source)]), buildProperties);
}
