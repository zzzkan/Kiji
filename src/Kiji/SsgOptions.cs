namespace Kiji;

/// <summary>
/// Options for the SSG build process.
/// </summary>
public sealed record SsgOptions
{
    /// <summary>
    /// Gets or sets the absolute path to the contents directory. The directory may not
    /// exist; content sources that need it (e.g. markdown) report their own errors.
    /// </summary>
    public required string ContentsPath
    {
        get;
        init => field = ValidateAbsolutePath(value);
    }

    /// <summary>
    /// Gets or sets the absolute path to the static assets directory. The directory may
    /// not exist; static file copying is skipped in that case.
    /// </summary>
    public required string StaticPath
    {
        get;
        init => field = ValidateAbsolutePath(value);
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
    /// Gets or sets the persistent image cache directory. When set, encoded image
    /// variants are kept here across builds and copied into the output, so unchanged
    /// images are not re-encoded. When null, variants are encoded directly into the output.
    /// </summary>
    public string? ImageCachePath
    {
        get;
        init => field = value is null ? null : ValidateAbsolutePath(value);
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
}
