using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Kiji.Components;

/// <summary>
/// Renders a page component inside its layout chain; Kiji's replacement for
/// Blazor's <c>RouteView</c> + <c>LayoutView</c>. The page's own
/// <see cref="LayoutAttribute"/> (<c>@layout</c>) takes precedence over
/// <see cref="DefaultLayout"/>, and layouts nest via <see cref="LayoutAttribute"/>
/// on the layout types themselves.
/// </summary>
internal sealed class PageView : ComponentBase
{
    // LayoutAttribute lookups cached per type; cleared on hot reload so
    // @layout edits apply in the dev server.
    private static readonly ConcurrentDictionary<Type, Type?> LayoutCache = new();

    [Parameter, EditorRequired]
    public Type PageType { get; set; } = default!;

    [Parameter]
    public IReadOnlyDictionary<string, object?>? PageParameters { get; set; }

    [Parameter]
    public Type? DefaultLayout { get; set; }

    internal static void ClearCache()
    {
        LayoutCache.Clear();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        RenderFragment fragment = RenderPage;

        var layoutType = GetLayoutType(PageType) ?? DefaultLayout;
        var seen = new HashSet<Type>();
        while (layoutType is not null)
        {
            if (!seen.Add(layoutType))
            {
                throw new InvalidOperationException(
                    $"Layout '{layoutType.FullName}' creates a circular layout chain.");
            }

            fragment = WrapInLayout(layoutType, fragment);
            layoutType = GetLayoutType(layoutType);
        }

        builder.AddContent(0, fragment);
    }

    private static Type? GetLayoutType(Type type)
    {
        return LayoutCache.GetOrAdd(type, static t => t.GetCustomAttribute<LayoutAttribute>()?.LayoutType);
    }

    private static RenderFragment WrapInLayout(Type layoutType, RenderFragment body)
    {
        return builder =>
        {
            builder.OpenComponent(0, layoutType);
            builder.AddComponentParameter(1, nameof(LayoutComponentBase.Body), body);
            builder.CloseComponent();
        };
    }

    private void RenderPage(RenderTreeBuilder builder)
    {
        builder.OpenComponent(0, PageType);
        if (PageParameters is not null)
        {
            foreach (var (name, value) in PageParameters)
            {
                builder.AddComponentParameter(1, name, value);
            }
        }

        builder.CloseComponent();
    }
}
