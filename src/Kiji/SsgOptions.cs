namespace Kiji;

/// <summary>
/// Options for the SSG build process.
/// </summary>
public sealed record SsgOptions
{
    /// <summary>
    /// Gets or sets the absolute path to the contents directory.
    /// </summary>
    public required string ContentsPath
    {
        get;
        init => field = ValidateExistingDirectory(value);
    }

    /// <summary>
    /// Gets or sets the absolute path to the static assets directory.
    /// </summary>
    public required string StaticPath
    {
        get;
        init => field = ValidateExistingDirectory(value);
    }

    /// <summary>
    /// Gets or sets the output path for all generated files.
    /// </summary>
    public required string OutputPath
    {
        get;
        init => field = ValidateAbsolutePath(value);
    }

    /// <summary>
    /// Gets or sets the subdirectory name under <see cref="OutputPath"/> for generated post assets. Defaults to "_assets".
    /// </summary>
    public string AssetsDirectoryName
    {
        get;
        init => field = ValidateAssetsDirectoryName(value);
    } = "_assets";

    private static string ValidateExistingDirectory(string value)
    {
        var fullPath = ValidateAbsolutePath(value);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
        }

        return fullPath;
    }

    private static string ValidateAbsolutePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("The path must be absolute.", nameof(value));
        }

        return Path.GetFullPath(value);
    }

    private static string ValidateAssetsDirectoryName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed is "." or "..")
        {
            throw new ArgumentException("AssetsDirectoryName must be a single directory name.", nameof(value));
        }

        if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal))
        {
            throw new ArgumentException("AssetsDirectoryName must be a single directory name.", nameof(value));
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("AssetsDirectoryName contains invalid file name characters.", nameof(value));
        }

        return trimmed;
    }
}
