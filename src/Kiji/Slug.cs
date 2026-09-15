using System.Text.RegularExpressions;

namespace Kiji;

/// <summary>A normalized, non-empty ASCII slug for a single route segment.</summary>
public sealed partial record Slug
{
    /// <summary>The normalized slug.</summary>
    public string Value { get; }

    private Slug(string value)
    {
        Value = value;
    }

    /// <summary>Creates a slug by normalizing a single route segment.</summary>
    public static Slug Create(string value)
    {
        return new Slug(Normalize(value));
    }

    /// <summary>Converts a single segment to lowercase ASCII letters, digits, and hyphens, rejecting an empty result.</summary>
    public static string Normalize(string value)
    {
        EnsureSingleRouteSegment(value);

        var trimmed = value.Trim().Trim('/', '\\');
        var normalized = trimmed.ToLowerInvariant();
        normalized = NonAlphaNumericRegex().Replace(normalized, "-");
        normalized = MultipleDashRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Slug '{value}' cannot be normalized to an empty canonical slug.");
        }

        return normalized;
    }

    /// <summary>Throws when a value differs from its normalized slug.</summary>
    public static void EnsureCanonical(string value)
    {
        var canonicalSlug = Normalize(value);
        if (!string.Equals(value, canonicalSlug, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Slug '{value}' is not canonical. Use '{canonicalSlug}'.");
        }
    }

    /// <summary>Rejects empty values or internal path separators after trimming whitespace and outer separators.</summary>
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

    /// <summary>Returns the normalized slug.</summary>
    public override string ToString()
    {
        return Value;
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleDashRegex();
}
