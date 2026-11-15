namespace Kiji.Assets;

/// <summary>Processor-relative variants and their separate site-relative directory.</summary>
internal sealed record ImageAsset(ProcessedImageInfo Info, string RelativeDirectory);
