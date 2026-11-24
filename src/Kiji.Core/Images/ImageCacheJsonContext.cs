using System.Text.Json.Serialization;

namespace Kiji.Images;

[JsonSerializable(typeof(CachedImageResult))]
[JsonSerializable(typeof(ImageCacheEnvelope))]
internal sealed partial class ImageCacheJsonContext : JsonSerializerContext;
