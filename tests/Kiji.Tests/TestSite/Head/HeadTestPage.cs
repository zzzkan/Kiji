using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Kiji.Tests.TestSite.Head;

public sealed class HeadTestPage : ComponentBase
{
    private bool _loaded;

    [Parameter]
    public string? Name { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await Task.Yield();
        _loaded = true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Name is not null)
        {
            var value = _loaded ? Name : "Loading";
            builder.OpenComponent<PageTitle>(0);
            builder.AddComponentParameter(1, nameof(PageTitle.ChildContent),
                (RenderFragment)(title => title.AddContent(0, value)));
            builder.CloseComponent();
            builder.OpenComponent<HeadContent>(2);
            builder.AddComponentParameter(3, nameof(HeadContent.ChildContent), (RenderFragment)(head =>
            {
                head.OpenElement(0, "meta");
                head.AddAttribute(1, "name", "description");
                head.AddAttribute(2, "content", value);
                head.CloseElement();
            }));
            builder.CloseComponent();
        }

        builder.OpenElement(4, "p");
        builder.AddContent(5, Name);
        builder.CloseElement();
    }
}
