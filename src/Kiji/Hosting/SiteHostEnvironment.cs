using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>
/// The site's read-only <see cref="IHostEnvironment"/>. Its values are fixed when the builder is
/// created, because they select the configuration sources and identify the build.
/// </summary>
/// <remarks>The built site owns the lazily created file provider and releases it when disposed.</remarks>
internal sealed class SiteHostEnvironment(string environmentName, string applicationName, string contentRootPath)
    : IHostEnvironment
{
    private readonly Lock _lock = new();
    private PhysicalFileProvider? _contentRootFileProvider;
    private bool _disposed;

    public string EnvironmentName
    {
        get => environmentName;
        set => throw new NotSupportedException(
            "The environment name selects the configuration files when the builder is created and cannot be changed. Set DOTNET_ENVIRONMENT instead.");
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
        get
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Created on first use: most sites never read it.
                return _contentRootFileProvider ??= new PhysicalFileProvider(contentRootPath);
            }
        }
        set => throw new NotSupportedException(
            "The content root is the site project directory supplied by Kiji's MSBuild targets and cannot be changed.");
    }

    internal void DisposeContentRootFileProvider()
    {
        lock (_lock)
        {
            _disposed = true;
            _contentRootFileProvider?.Dispose();
        }
    }
}
