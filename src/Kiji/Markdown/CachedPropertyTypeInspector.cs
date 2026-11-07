using System.Collections.Concurrent;
using YamlDotNet.Serialization;

namespace Kiji.Markdown;

/// <summary>
/// Memoizes property matches and their attribute-derived metadata. YamlDotNet otherwise
/// repeats the property search and reads the converter attribute for every key of every
/// front matter block.
/// </summary>
/// <remarks>
/// Deserialization passes no container, so only container-free lookups are cached. A lookup
/// that throws is not cached and throws again for the next document.
/// </remarks>
internal sealed class CachedPropertyTypeInspector(ITypeInspector inner) : ITypeInspector
{
    private readonly ConcurrentDictionary<(Type Type, string Name, bool IgnoreUnmatched, bool CaseInsensitive), CachedPropertyDescriptor?> _matches = new();

    public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container) => inner.GetProperties(type, container);

    public IPropertyDescriptor GetProperty(Type type, object? container, string name, bool ignoreUnmatched, bool caseInsensitivePropertyMatching)
    {
        if (container is not null)
        {
            return inner.GetProperty(type, container, name, ignoreUnmatched, caseInsensitivePropertyMatching);
        }

        var key = (type, name, ignoreUnmatched, caseInsensitivePropertyMatching);
        if (!_matches.TryGetValue(key, out var match))
        {
            var property = inner.GetProperty(type, null, name, ignoreUnmatched, caseInsensitivePropertyMatching);
            match = property is null ? null : new CachedPropertyDescriptor(property);
            match = _matches.GetOrAdd(key, match);
        }

        return match!;
    }

    public string GetEnumName(Type enumType, string name) => inner.GetEnumName(enumType, name);

    public string GetEnumValue(object enumValue) => inner.GetEnumValue(enumValue);

    public bool HasParseMethod(Type type) => inner.HasParseMethod(type);

    public object? Parse(string value, Type expectedType) => inner.Parse(value, expectedType);
}
