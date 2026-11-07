namespace Kiji;

/// <summary>Interprets environment variable flags for the dev server's status output.</summary>
internal static class FlagValue
{
    /// <summary>Returns true for <c>1</c> or <c>true</c> (case-insensitive).</summary>
    internal static bool IsTruthy(string? value)
    {
        return value is not null &&
            (string.Equals(value, "1", StringComparison.Ordinal) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }
}
