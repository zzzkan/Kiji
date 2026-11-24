using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kiji.Analyzers.Tests;

internal sealed class BuildPropertiesProvider(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new BuildProperties(properties);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => BuildProperties.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => BuildProperties.Empty;
}
