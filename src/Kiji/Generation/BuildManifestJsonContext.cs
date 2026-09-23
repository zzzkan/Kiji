using System.Text.Json.Serialization;

namespace Kiji.Generation;

/// <summary>
/// Source-generated JSON serialization for the build manifest (reflection-free and fast).
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(BuildManifest))]
internal sealed partial class BuildManifestJsonContext : JsonSerializerContext;
