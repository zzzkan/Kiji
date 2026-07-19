using Kiji.Components;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Kiji.Tests;

public sealed class HeadContentRegistryTests
{
    private static readonly RenderFragment FragmentA = static _ => { };
    private static readonly RenderFragment FragmentB = static _ => { };

    [Fact]
    public void Subscribe_PushesNullWhenNoProvider()
    {
        var registry = new HeadContentRegistry();
        var received = new List<RenderFragment?>();

        registry.Subscribe(received.Add);

        Assert.Single(received);
        Assert.Null(received[0]);
    }

    [Fact]
    public void Subscribe_PushesCurrentContentImmediately()
    {
        var registry = new HeadContentRegistry();
        registry.SetContent(CreateProvider(FragmentA));
        var received = new List<RenderFragment?>();

        registry.Subscribe(received.Add);

        Assert.Single(received);
        Assert.Same(FragmentA, received[0]);
    }

    [Fact]
    public void SetContent_LastRenderedProviderWins()
    {
        var registry = new HeadContentRegistry();
        var received = new List<RenderFragment?>();
        registry.Subscribe(received.Add);

        var first = CreateProvider(FragmentA);
        var second = CreateProvider(FragmentB);
        registry.SetContent(first);
        registry.SetContent(second);
        registry.SetContent(first);

        Assert.Same(FragmentB, received[^1]);
    }

    [Fact]
    public void SetContent_RepublishesWhenCurrentProviderUpdates()
    {
        var registry = new HeadContentRegistry();
        var received = new List<RenderFragment?>();
        registry.Subscribe(received.Add);

        var provider = CreateProvider(FragmentA);
        registry.SetContent(provider);
        SetChildContent(provider, FragmentB);
        registry.SetContent(provider);

        Assert.Same(FragmentB, received[^1]);
    }

    [Fact]
    public void RemoveProvider_FallsBackToPreviousProvider()
    {
        var registry = new HeadContentRegistry();
        var received = new List<RenderFragment?>();
        registry.Subscribe(received.Add);

        var first = CreateProvider(FragmentA);
        var second = CreateProvider(FragmentB);
        registry.SetContent(first);
        registry.SetContent(second);
        registry.RemoveProvider(second);

        Assert.Same(FragmentA, received[^1]);
    }

    [Fact]
    public void RemoveProvider_LastProviderRemovedPushesNull()
    {
        var registry = new HeadContentRegistry();
        var received = new List<RenderFragment?>();
        registry.Subscribe(received.Add);

        var provider = CreateProvider(FragmentA);
        registry.SetContent(provider);
        registry.RemoveProvider(provider);

        Assert.Null(received[^1]);
    }

    [Fact]
    public void Subscribe_SecondSubscriberThrows()
    {
        var registry = new HeadContentRegistry();
        registry.Subscribe(static _ => { });

        Assert.Throws<InvalidOperationException>(() => registry.Subscribe(static _ => { }));
    }

    private static HeadContent CreateProvider(RenderFragment? fragment)
    {
        var provider = new HeadContent();
        SetChildContent(provider, fragment);
        return provider;
    }

    private static void SetChildContent(HeadContent provider, RenderFragment? fragment)
    {
        ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(HeadContent.ChildContent)] = fragment,
        }).SetParameterProperties(provider);
    }
}
