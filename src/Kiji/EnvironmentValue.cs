namespace Kiji;

/// <summary>
/// Reads flag values, including the <c>DOTNET_WATCH_*</c> / <c>NO_COLOR</c> environment
/// variables the dev server honors and Kiji's process-local publish settings.
/// </summary>
internal static class EnvironmentValue
{
    /// <summary>
    /// Whether a flag-style variable is on. Only <c>1</c> and <c>true</c> count, matching
    /// what MSBuild writes for a boolean property.
    /// </summary>
    internal static bool IsTruthy(string? value)
    {
        return value is not null &&
            (string.Equals(value, "1", StringComparison.Ordinal) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }
}
