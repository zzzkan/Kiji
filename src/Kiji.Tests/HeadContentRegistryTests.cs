using Kiji.Components;
using Kiji.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Xunit;

namespace Kiji.Tests;

public sealed class HeadContentRegistryTests
{
    [Fact]
    public void Providers_ComposeUpdateAndRemoveInRegistrationOrder()
    {
        var registry = new HeadContentRegistry();
        RenderFragment? current = null;
        var rendered = new List<string>();
        var common = Create("css");
        var metadata = Create("charset");
        var page = Create("page");
        registry.SetContent(common);
        registry.SetContent(metadata);
        registry.Subscribe(fragment => current = fragment);
        AssertRendered("css", "charset");

        registry.SetContent(page);
        AssertRendered("css", "charset", "page");
        registry.SetContent(metadata);
        AssertRendered("css", "charset", "page");

        Set(page, "updated");
        registry.SetContent(page);
        AssertRendered("css", "charset", "updated");
        Set(page, "description");
        registry.SetContent(page);
        AssertRendered("css", "charset", "description");
        registry.RemoveProvider(common);
        AssertRendered("charset", "description");
        registry.RemoveProvider(page);
        AssertRendered("charset");
        registry.RemoveProvider(metadata);
        Assert.Null(current);

        StaticHeadContent Create(string value)
        {
            var provider = new StaticHeadContent();
            Set(provider, value);
            return provider;
        }

        void Set(StaticHeadContent provider, string value)
        {
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(StaticHeadContent.ChildContent)] = (RenderFragment)(_ => rendered.Add(value)),
            }).SetParameterProperties(provider);
        }

        void AssertRendered(params string[] expected)
        {
            rendered.Clear();
            using var builder = new RenderTreeBuilder();
            current!(builder);
            Assert.Equal(expected, rendered);
        }
    }
}
