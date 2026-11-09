using Kiji.Assets;
using Microsoft.Extensions.DependencyInjection;

namespace Kiji.Hosting;

/// <summary>Keeps Kiji's registration and scope validation when building a generic host.</summary>
internal sealed class KijiServiceProviderFactory(SiteBuilderContext context) : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        SiteServiceRegistration.Validate(containerBuilder, context.ManagedServices);
        containerBuilder.AddSingleton(new KijiServiceProviderStamp(SiteServiceRegistration.CollectAssemblies(containerBuilder)));
        var provider = containerBuilder.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        context.Runtime.Attach(provider);
        context.Runtime.Dependencies.Register("image-processor", () => ImageProcessorIdentity.Get(provider.GetRequiredService<IImageProcessor>()));
        return provider;
    }
}
