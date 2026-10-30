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
    private const string ReadOnlyMessage =
        "The host environment is fixed when the builder is created. Set DOTNET_ENVIRONMENT, or pass --environment while serving.";

    private readonly PhysicalFileProvider? _contentRootFileProvider = Directory.Exists(contentRootPath)
        ? new PhysicalFileProvider(contentRootPath) : null;

    public string EnvironmentName { get => environmentName; set => throw new NotSupportedException(ReadOnlyMessage); }

    public string ApplicationName { get => applicationName; set => throw new NotSupportedException(ReadOnlyMessage); }

    public string ContentRootPath { get => contentRootPath; set => throw new NotSupportedException(ReadOnlyMessage); }

    public IFileProvider ContentRootFileProvider
    {
        get => (IFileProvider?)_contentRootFileProvider ?? new NullFileProvider();
        set => throw new NotSupportedException(ReadOnlyMessage);
    }

    internal void DisposeContentRootFileProvider() => _contentRootFileProvider?.Dispose();
}
