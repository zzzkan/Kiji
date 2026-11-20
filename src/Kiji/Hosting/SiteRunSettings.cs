using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

internal sealed record SiteRunSettings(string? OutputPath, string EnvironmentName)
{
    private const string EnvironmentNameVariable = "DOTNET_ENVIRONMENT";

    internal bool Publishing => OutputPath is not null;

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
