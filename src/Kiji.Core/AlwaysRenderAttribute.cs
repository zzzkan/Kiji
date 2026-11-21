namespace Kiji;

/// <summary>Renders every page containing this component on every publish.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AlwaysRenderAttribute : Attribute;
