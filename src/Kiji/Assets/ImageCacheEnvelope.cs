namespace Kiji.Assets;

/// <summary>A checksum also protects dimensions and variant mappings, not just encoded bytes.</summary>
internal sealed record ImageCacheEnvelope(string Digest, string Payload);
