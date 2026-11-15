using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>How this process runs the site, read once when the builder is created.</summary>
/// <param name="OutputPath">Where to publish, or <see langword="null"/> to serve.</param>
/// <param name="EnvironmentName">The host environment name.</param>
internal sealed record SiteRunSettings(string? OutputPath, string EnvironmentName)
{
    /// <summary>The environment name variable read by the .NET generic host.</summary>
    private const string EnvironmentNameVariable = "DOTNET_ENVIRONMENT";

    internal bool Publishing => OutputPath is not null;

    /// <param name="hostConfiguration">
    /// Host configuration, separate from project-local site settings. Tests can supply
    /// in-memory configuration without mutating process-wide state.
    /// </param>
    /// <param name="runtimeMetadata">Typed runtimeconfig metadata supplied for this invocation.</param>
    /// <param name="args">Command-line arguments. Only <c>--environment</c> is read, and only while serving.</param>
    internal static SiteRunSettings FromExecutionContext(IConfiguration hostConfiguration,
        SiteRuntimeMetadata runtimeMetadata, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(hostConfiguration);
        ArgumentNullException.ThrowIfNull(runtimeMetadata);
        var outputPath = runtimeMetadata.OutputPath;
        var publishing = !string.IsNullOrWhiteSpace(outputPath);
        var environmentName = hostConfiguration[EnvironmentNameVariable];
        if (!publishing && string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = hostConfiguration["ASPNETCORE_ENVIRONMENT"];
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
