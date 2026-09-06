namespace Kiji;

/// <summary>
/// Reads the environment variables that carry build-host decisions into the app:
/// Kiji's own <c>KIJI_*</c> switches, set by the MSBuild targets, and the
/// <c>DOTNET_WATCH_*</c> / <c>NO_COLOR</c> flags the dev server honors.
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
