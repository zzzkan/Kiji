using System.Text.RegularExpressions;

namespace Kiji;

/// <summary>A normalized, non-empty ASCII slug for a single route segment.</summary>
public static partial class Slug
{
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

    private static void EnsureSingleRouteSegment(string value)
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

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleDashRegex();
}
