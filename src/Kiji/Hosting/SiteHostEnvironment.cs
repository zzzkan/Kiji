using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>
/// The site's read-only <see cref="IHostEnvironment"/>. Its values are fixed when the builder is
/// created, because they select the configuration sources and identify the build.
/// </summary>
internal sealed class SiteHostEnvironment(string environmentName, string applicationName, string contentRootPath)
    : IHostEnvironment
{
    private readonly PhysicalFileProvider? _contentRootFileProvider = Directory.Exists(contentRootPath)
        ? new PhysicalFileProvider(contentRootPath) : null;

    public string EnvironmentName
    {
        get => environmentName;
        set => throw new NotSupportedException(
            "The environment name selects the configuration files when the builder is created and cannot be changed. Set DOTNET_ENVIRONMENT, or pass --environment while serving.");
    }

    public string ApplicationName
    {
        get => applicationName;
        set => throw new NotSupportedException("The application name is the entry assembly name and cannot be changed.");
    }

    public string ContentRootPath
    {
        get => contentRootPath;
        set => throw new NotSupportedException(
            "The content root is the site project directory supplied by Kiji's MSBuild targets and cannot be changed.");
    }

    public IFileProvider ContentRootFileProvider
    {
        get => (IFileProvider?)_contentRootFileProvider ?? new NullFileProvider();
        set => throw new NotSupportedException(
            "The content root is the site project directory supplied by Kiji's MSBuild targets and cannot be changed.");
    }

    internal void DisposeContentRootFileProvider()
    {
        _contentRootFileProvider?.Dispose();
    }
}
