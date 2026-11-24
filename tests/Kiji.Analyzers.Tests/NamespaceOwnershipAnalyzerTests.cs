using Microsoft.CodeAnalysis;
using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class NamespaceOwnershipAnalyzerTests
{
    private static readonly Dictionary<string, string> PublicApi = new() { ["KijiPublicApi"] = "true" };

    [Fact]
    public async Task ReportsANamespaceDeclaredInTwoLibraries()
    {
        var feeds = Library("Kiji.Feeds", "namespace Kiji.Shared; public sealed class Feed { }");
        var sitemaps = Library("Kiji.Sitemaps", "namespace Kiji.Shared; public sealed class Sitemap { }");

        var diagnostics = await PackageDiagnosticsAsync("namespace Kiji; public static class StaticSite { }", feeds, sitemaps);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR0008", diagnostic.Id);
        Assert.Contains("Kiji.Shared is declared in both Kiji.Feeds and Kiji.Sitemaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowsOnlyStaticSiteBesideTheCoreRootNamespace()
    {
        var core = Library("Kiji.Core", "namespace Kiji; public sealed class SiteInfo { }");

        var allowed = await PackageDiagnosticsAsync("namespace Kiji; public static class StaticSite { }", core);
        var extra = await PackageDiagnosticsAsync("namespace Kiji { public static class StaticSite { } public static class Extra { } }", core);

        Assert.Empty(allowed);
        Assert.Equal("KR0008", Assert.Single(extra).Id);
    }

    private static PortableExecutableReference Library(string name, string source)
    {
        using var image = new MemoryStream();
        var result = AnalyzerRun.Compile(name, [($"{name}.cs", source)]).Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static Task<IReadOnlyList<Diagnostic>> PackageDiagnosticsAsync(string source, params MetadataReference[] libraries) =>
        AnalyzerRun.DiagnosticsAsync(new NamespaceOwnershipAnalyzer(), AnalyzerRun.Compile("Kiji", [("Kiji.cs", source)], libraries), PublicApi);
}
