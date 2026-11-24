using System.Text.Json.Serialization;
using Kiji.Incremental;

namespace Kiji.Testing;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(BuildManifest))]
internal sealed partial class BuildManifestJsonContext : JsonSerializerContext;
