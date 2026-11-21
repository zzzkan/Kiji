namespace Kiji.Assets;

/// <summary>The source size and its variants; the largest variant is the default <c>src</c>.</summary>
public sealed record ProcessedImageInfo
{
    /// <summary>Source width in pixels.</summary>
    public required int OriginalWidth { get; init; }

    /// <summary>Source height in pixels.</summary>
    public required int OriginalHeight { get; init; }

    /// <summary>Variants, ascending by width.</summary>
    public required IReadOnlyList<ImageVariant> Variants { get; init; }
}
