using System.Text.Json.Serialization;

namespace Kiji.Assets;

[JsonSerializable(typeof(CachedImageResult))]
[JsonSerializable(typeof(ImageCacheEnvelope))]
internal sealed partial class ImageCacheJsonContext : JsonSerializerContext;
