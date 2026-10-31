using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

internal sealed class MarkdownDateTimeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        return valueType == typeof(DateTimeOffset) || valueType == typeof(DateTime);
    }

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null && scalar.Style == ScalarStyle.Plain
            && (scalar.Value.Length == 0 || scalar.Value == "~" || scalar.Value.Equals("null", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var date = DateTimeOffset.Parse(scalar.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return (underlying ?? type) == typeof(DateTimeOffset) ? (object)date : date.UtcDateTime;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
        => throw new NotSupportedException("Markdown front matter is read-only.");
}
