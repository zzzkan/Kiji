using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Kiji.Hosting;

/// <summary>
/// Converts route-value objects (typically anonymous types) into route parameter dictionaries.
/// </summary>
internal static class RouteValues
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    internal static Dictionary<string, string> ToDictionary(object values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values is IReadOnlyDictionary<string, string> dictionary)
        {
            return new Dictionary<string, string>(dictionary, StringComparer.Ordinal);
        }

        var properties = PropertyCache.GetOrAdd(
            values.GetType(),
            static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

        var result = new Dictionary<string, string>(properties.Length, StringComparer.Ordinal);
        foreach (var property in properties)
        {
            var value = Convert.ToString(property.GetValue(values), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Route value '{property.Name}' on '{values.GetType().Name}' resolved to null or whitespace.");
            }

            result.Add(property.Name, value);
        }

        return result;
    }
}
