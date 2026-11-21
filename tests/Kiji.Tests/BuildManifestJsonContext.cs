using System.Text.Json.Serialization;
using Kiji.Generation;

namespace Kiji.Tests;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(BuildManifest))]
internal sealed partial class BuildManifestJsonContext : JsonSerializerContext;
