using Xunit;

namespace Kiji.RepositoryTests;

public sealed class ProjectBoundaryTests
{
    private const string GraphTarget = "KijiCheckProjectGraph";

    [Fact]
    public void LibraryMayReferenceCore()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core")
            .Library("Kiji.Feeds", Reference("Kiji.Core"));

        AssertPasses(workspace.RunTarget("src/Kiji.Feeds/Kiji.Feeds.csproj", GraphTarget));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" KijiLibrary=\"true\"")]
    [InlineData(" TreatAsUsed=\"true\" PrivateAssets=\"all\"")]
    public void LibraryMayNotReferenceAnotherLibraryWhateverItsMetadata(string metadata)
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core")
            .Library("Kiji.Markdown", Reference("Kiji.Core"))
            .Library("Kiji.Feeds", Reference("Kiji.Core") + Reference("Kiji.Markdown", metadata));

        AssertFails(workspace.RunTarget("src/Kiji.Feeds/Kiji.Feeds.csproj", GraphTarget), "KR0003", "may not reference Kiji.Markdown");
    }

    [Fact]
    public void TestProjectMayNotReferenceAnotherLibrary()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core")
            .Library("Kiji.Markdown", Reference("Kiji.Core"))
            .Library("Kiji.Feeds", Reference("Kiji.Core"))
            .Project("tests/Kiji.Feeds.Tests/Kiji.Feeds.Tests.csproj", Reference("Kiji.Feeds") + Reference("Kiji.Markdown"));

        AssertFails(workspace.RunTarget("tests/Kiji.Feeds.Tests/Kiji.Feeds.Tests.csproj", GraphTarget), "KR0003", "may not reference Kiji.Markdown");
    }

    [Fact]
    public void LibraryNeedsItsOwnTestProject()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core")
            .Project("src/Kiji.Feeds/Kiji.Feeds.csproj", Reference("Kiji.Core"));

        AssertFails(workspace.RunTarget("src/Kiji.Feeds/Kiji.Feeds.csproj", GraphTarget), "KR0003", "has no tests/Kiji.Feeds.Tests");
    }

    [Theory]
    [InlineData("samples/Kiji.Sample/Kiji.Sample.csproj")]
    [InlineData("src/Nested/Kiji.Sample/Kiji.Sample.csproj")]
    [InlineData("tests/Kiji.Other/Kiji.Sample.csproj")]
    public void ProjectLivesInARootFolderNamedAfterIt(string path)
    {
        using var workspace = BuildWorkspace.Create().Project(path, "");

        AssertFails(workspace.RunTarget(path, GraphTarget), "KR0003", "must live in");
    }

    [Fact]
    public void LibraryUsesOnlyThePackagesAssignedToIt()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core", """<PackageReference Include="Markdig" />""")
            .Library("Kiji.Markdown", """<PackageReference Include="Markdig" />""");

        AssertFails(workspace.RunTarget("src/Kiji.Core/Kiji.Core.csproj", "KijiCheckPackageReferences"), "KR0006", "may not reference the package Markdig");
        AssertPasses(workspace.RunTarget("src/Kiji.Markdown/Kiji.Markdown.csproj", "KijiCheckPackageReferences"));
    }

    [Fact]
    public void LibraryHasNoRazorFiles()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core")
            .Write("src/Kiji.Core/Components/Card.razor", "<p>card</p>");

        AssertFails(workspace.RunTarget("src/Kiji.Core/Kiji.Core.csproj", "KijiRequireHandWrittenComponents"), "KR0002", "Card.razor");
    }

    [Fact]
    public void PackageHasNoTransitiveTargets()
    {
        using var workspace = BuildWorkspace.Create()
            .Library("Kiji.Core", """<None Include="Kiji.targets" Pack="true" PackagePath="buildTransitive/" />""")
            .Write("src/Kiji.Core/Kiji.targets", "<Project />")
            .Write("src/Kiji.Core/Directory.Build.props", """
                <Project>
                  <Import Project="../Directory.Build.props" />
                  <PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>
                </Project>
                """);

        AssertFails(workspace.RunTarget("src/Kiji.Core/Kiji.Core.csproj", "KijiCheckPackageLayout"), "KR0005", "buildTransitive");
    }

    private static string Reference(string library, string metadata = "")
    {
        var path = library.EndsWith(".Tests", StringComparison.Ordinal) ? $"../../tests/{library}/{library}.csproj" : $"../../src/{library}/{library}.csproj";
        return $"""    <ProjectReference Include="{path}"{metadata} />""" + Environment.NewLine;
    }

    private static void AssertPasses((int ExitCode, string Output) result) =>
        Assert.True(result.ExitCode == 0, result.Output);

    private static void AssertFails((int ExitCode, string Output) result, string code, string text)
    {
        Assert.True(result.ExitCode != 0, result.Output);
        Assert.Contains($"error {code}", result.Output, StringComparison.Ordinal);
        Assert.Contains(text, result.Output, StringComparison.Ordinal);
    }
}
