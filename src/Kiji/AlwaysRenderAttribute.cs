namespace Kiji;

/// <summary>Renders every page containing this component on every publish.</summary>
/// <remarks>Applies to pages, layouts, and child components, including derived component types.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AlwaysRenderAttribute : Attribute;
