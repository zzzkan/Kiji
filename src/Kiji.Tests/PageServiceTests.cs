using System.Reflection;
using System.Reflection.Emit;
using Kiji.Feeds;
using Kiji.Tests.TestSite.PageServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Xunit;

namespace Kiji.Tests;

public sealed class PageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kiji-services-{Guid.NewGuid():N}");

    [Fact]
    public async Task Renders_ShareWithinPage_IsolateConcurrentPages_AndDispose()
    {
        var probe = new ServiceProbe { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var app = CreateApp(probe);
        app.AddPages<ServicePage>(_ => [new { Key = "first" }, new { Key = "second" }]);
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
            Assert.Equal(new Uri(app.Info.BaseUrl, $"services/{service.Key}/"), service.Page.Url);
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
        await using var app = CreateApp(probe);
        app.AddPages<ServicePage>(_ => [new { Key = "failure", Fail = true }]);
        var request = Assert.Single(app.CreateSnapshot().Pages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RenderPageAsync(request, CancellationToken.None));
        var service = Assert.Single(probe.Created);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(1, service.Dependency.DisposeCount);
    }

    [Theory]
    [InlineData(typeof(SiteInfo))]
    [InlineData(typeof(PageInfo))]
    [InlineData(typeof(ResolvedSitePaths))]
    [InlineData(typeof(PageBuildInputs))]
    [InlineData(typeof(Generation.ContentFileRegistry))]
    [InlineData(typeof(ContentDictionary<ServiceProbe>))]
    [InlineData(typeof(NavigationManager))]
    [InlineData(typeof(IJSRuntime))]
    [InlineData(typeof(IComponentActivator))]
    public async Task Registration_RejectsAdditionalManagedServices(Type serviceType)
    {
        await using var app = CreateApp();
        app.UseContentSource<ServiceProbe>(_ => []);
        app.UseServices(services => services.Add(services.Single(descriptor => descriptor.ServiceType == serviceType)));
        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());
        Assert.Contains(serviceType.FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("managed by Kiji", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    [InlineData("keyed")]
    [InlineData("open-generic")]
    [InlineData("unregistered-content")]
    [InlineData("provider")]
    public async Task Registration_ValidatesTheFinalManagedRegistrations(string operation)
    {
        await using var app = CreateApp();
        IServiceCollection? captured = null;
        app.UseServices(services =>
        {
            captured = services;
            switch (operation)
            {
                case "remove": services.RemoveAll<PageInfo>(); break;
                case "replace": services.Replace(ServiceDescriptor.Singleton(app.Info)); break;
                case "clear": services.Clear(); break;
                case "keyed": services.AddKeyedSingleton("alternate", app.Info); break;
                case "open-generic": services.AddSingleton(typeof(ContentDictionary<>), typeof(ContentDictionary<>)); break;
                case "unregistered-content": services.AddSingleton<ContentDictionary<ServiceProbe>>(_ => throw new NotSupportedException()); break;
                case "provider": services.AddSingleton<IServiceProvider>(_ => throw new NotSupportedException()); break;
            }
        });
        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateSnapshot());
        Assert.Contains("managed by Kiji", exception.Message, StringComparison.Ordinal);
        Assert.True(captured!.IsReadOnly);
    }

    [Fact]
    public async Task Configuration_IsDeferredOrderedAndFrozen_AndFactoriesResolveSettledPaths()
    {
        await using var app = CreateApp();
        IServiceCollection? captured = null;
        var calls = new List<int>();
        var created = 0;
        app.UseServices(services =>
        {
            calls.Add(1);
            captured = services;
            services.AddSingleton("first");
            services.AddSingleton(provider =>
            {
                created++;
                Assert.EndsWith(Path.Combine(".kiji", "dev-site"), provider.GetRequiredService<ResolvedSitePaths>().OutputDirectory, StringComparison.Ordinal);
                return new RenderDependency();
            });
        });
        app.UseServices(services =>
        {
            calls.Add(2);
            services.Replace(ServiceDescriptor.Singleton("second"));
            // Existing managed registrations are visible to ordinary extension methods.
            services.TryAddSingleton(app.Info);
        });
        Assert.Empty(calls);
        app.CreateSnapshot();
        app.CreateSnapshot();
        Assert.Equal([1, 2], calls);
        Assert.Equal(0, created);
        Assert.Equal("second", app.ServiceProvider.GetRequiredService<string>());
        var owned = app.ServiceProvider.GetRequiredService<RenderDependency>();
        Assert.Equal(1, created);
        Assert.Throws<InvalidOperationException>(() => captured!.AddSingleton("late"));
        Assert.Throws<InvalidOperationException>(() => app.UseServices(_ => { }));
        await app.DisposeAsync();
        Assert.Equal(1, owned.DisposeCount);
    }

    [Fact]
    public async Task MissingDependency_FailsBeforeRendering()
    {
        await using var app = CreateApp();
        app.UseServices(services => services.AddScoped<object, MissingDependencyService>());
        var exception = Assert.Throws<AggregateException>(() => app.CreateSnapshot());
        Assert.Contains(nameof(MissingDependencyService), exception.ToString(), StringComparison.Ordinal);
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
        var builder = assembly.DefineDynamicModule("Plugin").DefineType("PluginService", TypeAttributes.Public);
        var constructor = builder.DefineDefaultConstructor(MethodAttributes.Public);
        var create = builder.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(IServiceProvider)]);
        var il = create.GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        var type = builder.CreateType()!;
        var probe = new ServiceProbe();
        await using var app = CreateApp(probe);
        app.AddPages<ServicePage>(_ => [new { Key = "test" }]);
        app.UseServices(services =>
        {
            switch (registration)
            {
                case "type": services.Add(new ServiceDescriptor(typeof(object), type, ServiceLifetime.Singleton)); break;
                case "instance": services.AddSingleton(Activator.CreateInstance(type)!); break;
                case "factory": services.AddSingleton(type.GetMethod("Create")!.CreateDelegate<Func<IServiceProvider, object>>()); break;
            }
        });
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
        await using var app = CreateApp();
        app.UseServices(services => services.AddScoped<IDisposable, RenderDependency>());
        switch (consumer)
        {
            case "route":
                app.AddPages<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [new { Key = "test" }];
                });
                break;
            case "feed":
                app.AddRssFeed(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                break;
            case "artifact":
                app.AddArtifact("service.txt", static (_, context, _) =>
                {
                    _ = context.Services.GetRequiredService<IDisposable>();
                    return Task.CompletedTask;
                });
                break;
            case "loader":
                app.UseContentSource<ServiceProbe>(provider =>
                {
                    _ = provider.GetRequiredService<IDisposable>();
                    return [];
                });
                app.AddPages<ServicePage>(provider =>
                {
                    _ = provider.GetRequiredService<ContentDictionary<ServiceProbe>>().Count;
                    return [];
                });
                break;
        }
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => app.PublishAsync(Path.Combine(_root, "dist")));
        Assert.Contains("scoped service", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(IDisposable), exception.ToString(), StringComparison.Ordinal);
    }

    private StaticSite CreateApp(ServiceProbe? probe = null)
    {
        var app = StaticSite.Create([], new SiteExecutionPaths(_root));
        app.Info = new SiteInfo { Name = "Services", BaseUrl = new Uri("https://example.test/") };
        if (probe is not null)
        {
            app.UseContentSource<ServiceProbe>("probes", _ => [new ContentEntry<ServiceProbe>("0", probe, "stable")]);
            app.UseServices(services =>
            {
                services.AddScoped<RenderDependency>();
                services.AddScoped<IRenderService>(provider => new RenderService(
                    provider.GetRequiredService<ContentDictionary<ServiceProbe>>(),
                    provider.GetRequiredService<RenderDependency>(), provider.GetRequiredService<PageInfo>()));
            });
            app.UseDefaultLayout<ServiceLayout>();
        }
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
