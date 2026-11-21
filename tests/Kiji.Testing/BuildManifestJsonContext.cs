using System.Text.Json.Serialization;
using Kiji.Generation;

namespace Kiji.Testing;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(BuildManifest))]
internal sealed partial class BuildManifestJsonContext : JsonSerializerContext;
