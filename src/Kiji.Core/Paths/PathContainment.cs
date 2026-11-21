namespace Kiji.Paths;

internal static class PathContainment
{
    internal static string ResolveUnderRoot(string root, string relativePath, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException(
                $"{description} '{relativePath}' must be relative to the output directory.");
        }

        var normalizedRoot = Normalize(root);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (string.Equals(normalizedRoot, fullPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Contains(normalizedRoot, fullPath))
        {
            throw new InvalidOperationException(
                $"{description} '{relativePath}' escapes the output directory.");
        }

        return fullPath;
    }

    internal static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, child, comparison)
            || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison);
    }
}
