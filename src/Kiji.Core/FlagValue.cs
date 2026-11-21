namespace Kiji;

internal static class FlagValue
{
    internal static bool IsTruthy(string? value)
    {
        return value is not null &&
            (string.Equals(value, "1", StringComparison.Ordinal) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }
}
