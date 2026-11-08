using Kiji.Assets;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Hosting;

/// <summary>Keeps Kiji's registration and scope validation when building a generic host.</summary>
internal sealed class KijiServiceProviderFactory(SiteBuilderContext context) : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        if (context.Environment.EnvironmentName != context.EnvironmentName
            || context.Environment.ContentRootPath != context.ContentRootPath)
        {
            throw new InvalidOperationException("Do not change the Kiji host environment name or content root after CreateBuilder; they select the project's settings and execution paths.");
        }
        SiteServiceRegistration.Validate(containerBuilder, context.ManagedServices);
        containerBuilder.AddSingleton(new KijiServiceProviderStamp(SiteServiceRegistration.CollectAssemblies(containerBuilder)));
        var provider = containerBuilder.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        context.Runtime.Attach(provider);
        context.Runtime.Dependencies.Register("image-processor", () => ImageProcessorIdentity.Get(provider.GetRequiredService<IImageProcessor>()));
        return provider;
    }
}
