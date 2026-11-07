using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>How this process runs the site, read once when the builder is created.</summary>
/// <param name="OutputPath">Where to publish, or <see langword="null"/> to serve.</param>
/// <param name="EnvironmentName">The host environment name.</param>
internal sealed record SiteRunSettings(string? OutputPath, string EnvironmentName)
{
    /// <summary>Where to generate the site. Set by Kiji's MSBuild targets on publish.</summary>
    private const string OutputPathProperty = "Kiji.OutputPath";

    /// <summary>The environment name variable read by the .NET generic host.</summary>
    private const string EnvironmentNameVariable = "DOTNET_ENVIRONMENT";

    internal bool Publishing => OutputPath is not null;

    /// <param name="environment">
    /// Reads an environment variable. Injected so tests can exercise both modes without
    /// mutating the process environment, which parallel test classes share.
    /// </param>
    /// <param name="runtimeData">Reads process-local runtimeconfig properties supplied for this invocation.</param>
    /// <param name="args">Command-line arguments. Only <c>--environment</c> is read, and only while serving.</param>
    internal static SiteRunSettings FromExecutionContext(Func<string, string?> environment,
        Func<string, object?> runtimeData, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runtimeData);
        var outputPath = runtimeData(OutputPathProperty) as string;
        var publishing = !string.IsNullOrWhiteSpace(outputPath);
        var environmentName = environment(EnvironmentNameVariable);
        if (!publishing && string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = environment("ASPNETCORE_ENVIRONMENT");
        }
        if (!publishing && args is { Length: > 0 })
        {
            // Use ASP.NET Core's command-line provider only to select the host environment.
            // Application configuration remains project-local.
            using var commandLine = new ConfigurationManager();
            commandLine.AddCommandLine(args);
            environmentName = commandLine[HostDefaults.EnvironmentKey] ?? environmentName;
        }

        return new SiteRunSettings(
            publishing ? outputPath : null,
            !string.IsNullOrWhiteSpace(environmentName) ? environmentName
                : publishing ? Environments.Production : Environments.Development);
    }
}
