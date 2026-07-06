using System.Text.RegularExpressions;

namespace Kiji;

public sealed partial record Slug
{
    public string Value { get; }

    private Slug(string value)
    {
        Value = value;
    }

    public static Slug Create(string value)
    {
        return new Slug(Normalize(value));
    }

    public static string Normalize(string value)
    {
        EnsureSingleRouteSegment(value);

        var trimmed = value.Trim().Trim('/', '\\');
        var normalized = trimmed.ToLowerInvariant();
        normalized = normalized.Replace(".net", "dot-net", StringComparison.Ordinal);
        normalized = normalized.Replace("c#", "c-sharp", StringComparison.Ordinal);
        normalized = NonAlphaNumericRegex().Replace(normalized, "-");
        normalized = MultipleDashRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Slug '{value}' cannot be normalized to an empty canonical slug.");
        }

        return normalized;
    }

    public static void EnsureCanonical(string value)
    {
        var canonicalSlug = Normalize(value);
        if (!string.Equals(value, canonicalSlug, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Slug '{value}' is not canonical. Use '{canonicalSlug}'.");
        }
    }

    public static void EnsureSingleRouteSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Slug cannot be empty.");
        }

        var trimmed = value.Trim().Trim('/', '\\');
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Slug cannot be empty.");
        }

        if (trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Slug '{value}' must be a single route segment and cannot contain '/' or '\\'.");
        }
    }

    public static IReadOnlyDictionary<string, string> CreateNameMap(
        IEnumerable<string> values,
        string singularDisplayName,
        string pluralDisplayName)
    {
        var namesBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var slug = Normalize(value);
            if (namesBySlug.TryGetValue(slug, out var existingValue) &&
                !string.Equals(existingValue, value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{pluralDisplayName} '{existingValue}' and '{value}' both normalize to canonical {singularDisplayName} slug '{slug}'. Rename one of the {pluralDisplayName.ToLowerInvariant()} so each {singularDisplayName.ToLowerInvariant()} keeps a unique canonical URL.");
            }

            namesBySlug[slug] = value;
        }

        return namesBySlug;
    }

    public override string ToString()
    {
        return Value;
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleDashRegex();
}
