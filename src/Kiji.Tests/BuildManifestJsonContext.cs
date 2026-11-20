using System.Text.Json.Serialization;
using Kiji.Generation;

namespace Kiji.Tests;

/// <summary>
/// The serializer's own reading of the manifest records: the format <see cref="BuildManifestSerializer"/> must match.
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(BuildManifest))]
internal sealed partial class BuildManifestJsonContext : JsonSerializerContext;
