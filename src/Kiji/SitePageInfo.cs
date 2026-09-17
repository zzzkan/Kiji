namespace Kiji;

/// <summary>
/// A page in the generated site, as exposed to artifact writers.
/// </summary>
public sealed record SitePageInfo
{
    /// <summary>Creates generated page metadata.</summary>
    /// <param name="RelativePath">The path relative to <see cref="SiteInfo.BaseUrl"/>, e.g. <c>blog/my-post/</c>.</param>
    /// <param name="OutputRelativePath">The output file path relative to the output directory.</param>
    public SitePageInfo(string RelativePath, string OutputRelativePath)
    {
        global::Kiji.RelativePath.Validate(RelativePath, nameof(RelativePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRelativePath);

        this.RelativePath = RelativePath;
        this.OutputRelativePath = OutputRelativePath;
    }

    /// <summary>The path relative to <see cref="SiteInfo.BaseUrl"/>.</summary>
    public string RelativePath { get; }

    /// <summary>The output file path relative to the output directory.</summary>
    public string OutputRelativePath { get; }
}
