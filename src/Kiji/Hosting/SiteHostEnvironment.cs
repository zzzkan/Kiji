using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kiji.Hosting;

/// <summary>
/// The site's <see cref="IHostEnvironment"/>. Names can change until the site is built;
/// the content root is always the project directory supplied by MSBuild.
/// </summary>
/// <remarks>The built site owns the lazily created file provider and releases it when disposed.</remarks>
internal sealed class SiteHostEnvironment(string environmentName, string applicationName, string contentRootPath)
    : IHostEnvironment
{
    private readonly Lock _lock = new();
    private PhysicalFileProvider? _contentRootFileProvider;
    private bool _frozen;
    private bool _disposed;

    public string EnvironmentName
    {
        get;
        set
        {
            EnsureConfigurable();
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    } = environmentName;

    public string ApplicationName
    {
        get;
        set
        {
            EnsureConfigurable();
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = applicationName;

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

    internal void Freeze() => _frozen = true;

    internal void DisposeContentRootFileProvider()
    {
        lock (_lock)
        {
            _disposed = true;
            _contentRootFileProvider?.Dispose();
        }
    }

    private void EnsureConfigurable()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("The host environment cannot be changed after the site is built.");
        }
    }
}
