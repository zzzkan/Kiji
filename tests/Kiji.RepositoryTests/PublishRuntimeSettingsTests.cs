using System.Xml.Linq;
using Xunit;

namespace Kiji.RepositoryTests;

public sealed class PublishRuntimeSettingsTests
{
    private const string Targets = "src/Kiji/MSBuild/Kiji.targets";

    private static readonly string[] Measured = ["Kiji.OutputPath", "Kiji.JitProfileDirectory", "System.Runtime.TieredPGO"];

    private static readonly Dictionary<string, string> Rejected = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System.Runtime.TieredCompilation"] = "compiling optimized code costs more than a short process gets back",
        ["System.Runtime.TieredCompilation.QuickJit"] = "compiling optimized code costs more than a short process gets back",
        ["System.Runtime.TieredCompilation.QuickJitForLoops"] = "compiling optimized code costs more than a short process gets back",
        ["System.Runtime.TieredCompilation.CallCountThreshold"] = "compiling optimized code costs more than a short process gets back",
        ["System.Runtime.TieredCompilation.CallCountingDelayMs"] = "compiling optimized code costs more than a short process gets back",
        ["System.GC.Concurrent"] = "no difference",
        ["System.GC.Gen0Size"] = "no difference",
        ["Microsoft.Extensions.DependencyInjection.DisableDynamicEngine"] = "no difference",
        ["DOTNET_EnableWriteXorExecute"] = "faster, but removes a runtime mitigation; a site owner can set it, Kiji does not",
    };

    [Fact]
    public void PublishProcessUsesOnlyMeasuredRuntimeSettings()
    {
        var document = XDocument.Parse(RepositoryFiles.ReadText(Targets));
        var options = document.Descendants()
            .Where(element => element.Name.LocalName == "_KijiHostConfigurationOption")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(name => name is not null && !name.StartsWith("@(", StringComparison.Ordinal))
            .Select(name => name!)
            .ToList();
        var environment = document.Descendants()
            .Where(element => element.Name.LocalName == "Exec")
            .Select(element => (string?)element.Attribute("EnvironmentVariables") ?? "")
            .Where(value => value.Length > 0)
            .ToList();

        var failures = options.Where(option => !Measured.Contains(option, StringComparer.Ordinal))
            .Select(option => Rejected.TryGetValue(option, out var reason)
                ? $"{option} was measured and rejected for the publish process: {reason}."
                : $"{option} is a new publish runtime setting. Measure it with the measure-performance skill, then add it to {nameof(Measured)}.")
            .Concat(environment.Select(value => $"Kiji's publish process sets no environment variables ({value}); {Rejected["DOTNET_EnableWriteXorExecute"]}."))
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(Measured.Order(StringComparer.Ordinal), options.Order(StringComparer.Ordinal));
    }
}
