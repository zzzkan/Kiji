namespace Kiji;

/// <summary>A generated page.</summary>
public sealed record SitePageInfo
{
    /// <summary>Creates a generated page.</summary>
    /// <param name="RelativePath">The page path under <see cref="SiteInfo.BaseUrl"/>, such as <c>blog/my-post/</c>.</param>
    /// <param name="OutputRelativePath">The file path in the output directory.</param>
    public SitePageInfo(string RelativePath, string OutputRelativePath)
    {
        global::Kiji.RelativePath.Validate(RelativePath, nameof(RelativePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputRelativePath);

        this.RelativePath = RelativePath;
        this.OutputRelativePath = OutputRelativePath;
    }

    /// <summary>The page path under <see cref="SiteInfo.BaseUrl"/>.</summary>
    public string RelativePath { get; }

    /// <summary>The file path in the output directory.</summary>
    public string OutputRelativePath { get; }
}
