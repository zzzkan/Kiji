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
    public void Providers_UpdateAndFallBackWithoutLettingOlderRendersTakeOver()
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
        SetChildContent(second, FragmentA);
        registry.SetContent(second);
        Assert.Same(FragmentA, received[^1]);
        SetChildContent(first, FragmentB);
        registry.RemoveProvider(second);
        Assert.Same(FragmentB, received[^1]);
        registry.RemoveProvider(first);
        Assert.Null(received[^1]);
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
