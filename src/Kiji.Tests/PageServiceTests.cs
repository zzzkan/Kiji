using System.Reflection;
using System.Reflection.Emit;
using Kiji.Feeds;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kiji.Tests;

public sealed class PageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-services-{Guid.NewGuid():N}");

    [Fact]
    public async Task Renders_ShareWithinPage_IsolateConcurrentPages_AndDispose()
    {
        var probe = new ServiceProbe { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var builder = CreateBuilder(probe);
        builder.AddPageParameters<ServicePage>(_ => [new { Key = "first" }, new { Key = "second" }]);
        await using var app = builder.Build();
        var requests = app.CreateSnapshot().Pages;

        var renders = requests.Select(request => app.RenderPageAsync(request, CancellationToken.None)).ToArray();
        try
        {
            // Both renders reach their asynchronous lifecycle before either can finish.
            Assert.Equal(2, probe.Created.Count);
            Assert.All(probe.Created, service => Assert.Equal(0, service.DisposeCount));
        }
        finally
        {
            probe.Release.SetResult();
        }
        await Task.WhenAll(renders);

        probe.Release = null;
        await app.RenderPageAsync(requests[0], CancellationToken.None);
        Assert.Equal(3, probe.Created.Count);
        Assert.Equal(3, probe.Created.Distinct().Count());
        Assert.Equal(3, probe.Created.Select(service => service.Page).Distinct().Count());
        foreach (var service in probe.Created)
        {
            Assert.Equal(1, service.DisposeCount);
            Assert.Equal(1, service.Dependency.DisposeCount);
            var places = probe.Reads.Where(read => ReferenceEquals(read.Service, service)).Select(read => read.Place);
            Assert.Contains("page", places);
            Assert.Contains("layout", places);
            Assert.Contains("child", places);
            Assert.Equal(new Uri(app.SiteInfo.BaseUrl, $"services/{service.Key}/"), service.Page.Url);
            var pageReads = probe.PageReads.Where(read => ReferenceEquals(read.Service, service)).ToArray();
            Assert.Equal(3, pageReads.Length);
            Assert.All(pageReads, read => Assert.Same(service.Page, read.Page));
        }
        Assert.Contains("firstfirst", await renders[0], StringComparison.Ordinal);
        Assert.Contains("secondsecond", await renders[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRender_DisposesBothAsyncAndSyncServices()
    {
        var probe = new ServiceProbe();
        var builder = CreateBuilder(probe);
        builder.AddPageParameters<ServicePage>(_ => [new { Key = "failure", Fail = true }]);
        await using var app = builder.Build();
        var request = Assert.Single(app.CreateSnapshot().Pages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RenderPageAsync(request, CancellationToken.None));
        var service = Assert.Single(probe.Created);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(1, service.Dependency.DisposeCount);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("keyed")]
    [InlineData("open-generic")]
    [InlineData("provider")]
    [InlineData("environment")]
    [InlineData("content")]
    [InlineData("configuration")]
    public void Build_ValidatesTheFinalManagedRegistrations(string operation)
    {
        var builder = CreateBuilder(new ServiceProbe());
        var services = builder.Services;
        switch (operation)
        {
            case "duplicate": services.Add(services.Single(descriptor => descriptor.ServiceType == typeof(PageInfo))); break;
            case "remove": services.RemoveAll<PageInfo>(); break;
            case "replace": services.Replace(ServiceDescriptor.Singleton(builder.SiteInfo)); break;
            case "keyed": services.AddKeyedSingleton("alternate", builder.SiteInfo); break;
            case "open-generic": services.AddSingleton(typeof(ContentDictionary<>), typeof(ContentDictionary<>)); break;
            case "provider": services.AddSingleton<IServiceProvider>(_ => throw new NotSupportedException()); break;
            case "environment": services.Replace(ServiceDescriptor.Singleton(builder.Environment)); break;
            case "content": services.RemoveAll<ContentDictionary<ServiceProbe>>(); break;
            case "configuration": services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()); break;
        }
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("managed by Kiji", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Services_AreVisibleToExtensionsAndFrozenByBuild_AndFactoriesResolveSettledPaths()
    {
        var builder = CreateBuilder();
        builder.Services.AddSingleton(provider => provider.GetRequiredService<ResolvedSitePaths>().OutputDirectory);
        // Managed registrations are present from the start, so ordinary extension methods see them.
        builder.Services.TryAddSingleton(new SiteInfo { Name = "Replacement", BaseUrl = new Uri("https://replacement.test/") });
        builder.Services.TryAddSingleton<IHostEnvironment>(new ReplacementEnvironment());
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Services.AddSingleton("late"));

        app.CreateSnapshot();
        Assert.Same(builder.SiteInfo, app.ServiceProvider.GetRequiredService<SiteInfo>());
        Assert.Same(builder.Environment, app.ServiceProvider.GetRequiredService<IHostEnvironment>());
        Assert.EndsWith(Path.Combine(".kiji", "dev-site"), app.ServiceProvider.GetRequiredService<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDependency_FailsBuild()
    {
        var builder = CreateBuilder();
        builder.Services.AddScoped<object, MissingDependencyService>();
        var exception = Assert.Throws<AggregateException>(() => builder.Build());
        Assert.Contains(nameof(MissingDependencyService), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicFactory_WithAStableHostModule_IsRejected()
    {
        var builder = CreateBuilder();
        var method = new DynamicMethod("Create", typeof(object), [typeof(IServiceProvider)], typeof(PageServiceTests).Module);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        builder.Services.AddSingleton(method.CreateDelegate<Func<IServiceProvider, object>>());

        var exception = Assert.Throws<NotSupportedException>(() => builder.Build());
        Assert.Contains("regular C# lambda or method", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("instance")]
    [InlineData("factory")]
    [InlineData("stable")]
    public async Task ServiceCode_OnlyReusesPagesWhenItsIdentityIsAvailable(string registration)
    {
        // Plugin code need not be referenced by a page assembly. If its identity cannot
        // be recorded, a registration must make the build conservative even before resolution.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"Plugin{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var typeBuilder = assembly.DefineDynamicModule("Plugin").DefineType("PluginService", TypeAttributes.Public);
        var constructor = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        var create = typeBuilder.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(IServiceProvider)]);
        var il = create.GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        var type = typeBuilder.CreateType()!;
        var probe = new ServiceProbe();
        var builder = CreateBuilder(probe);
        builder.AddPageParameters<ServicePage>(_ => [new { Key = "test" }]);
        switch (registration)
        {
            case "type": builder.Services.Add(new ServiceDescriptor(typeof(object), type, ServiceLifetime.Singleton)); break;
            case "instance": builder.Services.AddSingleton(Activator.CreateInstance(type)!); break;
            case "factory": builder.Services.AddSingleton(type.GetMethod("Create")!.CreateDelegate<Func<IServiceProvider, object>>()); break;
        }
        await using var app = builder.Build();
        var output = Path.Combine(_root, "dist");
        await app.PublishAsync(output);
        Assert.Single(probe.Created);
        var original = await File.ReadAllTextAsync(Path.Combine(output, "services/test/index.html"));
        await app.PublishAsync(output);
        Assert.Equal(registration == "stable" ? 1 : 2, probe.Created.Count);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(output, "services/test/index.html")));
    }

    [Theory]
    [InlineData("route")]
    [InlineData("feed")]
    [InlineData("artifact")]
    [InlineData("loader")]
    public async Task PageServices_CannotBeResolvedOutsidePage(string consumer)
    {
        var builder = CreateBuilder();
        builder.Services.AddScoped<IDisposable, RenderDependency>();
        switch (consumer)
        {
            case "route":
                builder.AddPageParameters<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [new { Key = "test" }];
                });
                break;
            case "feed":
                builder.AddRssFeed(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                break;
            case "artifact":
                builder.AddArtifact("service.txt", static (_, context, _) =>
                {
                    _ = context.Services.GetRequiredService<IDisposable>();
                    return Task.CompletedTask;
                });
                break;
            case "loader":
                builder.AddContentSource<ServiceProbe>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                builder.AddPageParameters<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<ContentDictionary<ServiceProbe>>().Count;
                    return [];
                });
                break;
        }
        await using var app = builder.Build();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("scoped service", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(IDisposable), exception.ToString(), StringComparison.Ordinal);
    }

    private StaticSiteBuilder CreateBuilder(ServiceProbe? probe = null)
    {
        var builder = TestSiteFactory.CreateBuilder(new SiteExecutionPaths(_root));
        builder.SiteInfo = new SiteInfo { Name = "Services", BaseUrl = new Uri("https://example.test/") };
        if (probe is not null)
        {
            builder.AddContentSource<ServiceProbe>("probes", _ => [new ContentEntry<ServiceProbe>("0", probe, "stable")]);
            builder.Services.AddScoped<RenderDependency>();
            builder.Services.AddScoped<IRenderService>(provider => new RenderService(
                provider.GetRequiredService<ContentDictionary<ServiceProbe>>(),
                provider.GetRequiredService<RenderDependency>(), provider.GetRequiredService<PageInfo>()));
            builder.UseDefaultLayout<ServiceLayout>();
        }
        return builder;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ReplacementEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Replacement";
        public string ApplicationName { get; set; } = "Replacement";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
