using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>Caches the reflection-derived converter type of a YAML property.</summary>
internal sealed class CachedPropertyDescriptor(IPropertyDescriptor inner) : IPropertyDescriptor
{
    private readonly Lazy<Type?> _converterType = new(() => inner.ConverterType);

    public string Name => inner.Name;

    public bool AllowNulls => inner.AllowNulls;

    public bool CanWrite => inner.CanWrite;

    public Type Type => inner.Type;

    public Type? TypeOverride
    {
        get => inner.TypeOverride;
        set => inner.TypeOverride = value;
    }

    public int Order
    {
        get => inner.Order;
        set => inner.Order = value;
    }

    public ScalarStyle ScalarStyle
    {
        get => inner.ScalarStyle;
        set => inner.ScalarStyle = value;
    }

    public bool Required => inner.Required;

    public Type? ConverterType => _converterType.Value;

    public T? GetCustomAttribute<T>() where T : Attribute => inner.GetCustomAttribute<T>();

    public IObjectDescriptor Read(object target) => inner.Read(target);

    public void Write(object target, object? value) => inner.Write(target, value);
}
