namespace Kiji;

/// <summary>The absolute directories resolved for the current execution.</summary>
internal sealed record ResolvedSitePaths
{
    internal string? AssetManifestPath { get; init; }
    internal string? AssetEndpointsPath { get; init; }

    /// <summary>The absolute directory for generated files.</summary>
    public required string OutputDirectory
    {
        get;
        init => field = ValidateAbsolutePath(value);
    }

    /// <summary>The absolute persistent image cache directory, or null to generate images directly in the output.</summary>
    public string? ImageCacheDirectory
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
