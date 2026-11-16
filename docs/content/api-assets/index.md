---
title: Kiji.Assets namespace
navTitle: Kiji.Assets
description: Image processing options and the image processor interface.
order: 130
section: API reference
---

## `ImageProcessorOptions`

Settings for the default image processor, configured with the
[options pattern](https://learn.microsoft.com/dotnet/core/extensions/options):

```csharp
builder.Services.Configure<ImageProcessorOptions>(options =>
{
    options.Widths = [480, 960];
    options.Quality = 75;
});
```

| Property                     | Default | Behavior                                                                                                                            |
| ---------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `IReadOnlyList<int>? Widths` | `null`  | Widths of the smaller variants. `null` uses 320, 640, 960, and 1280. Widths at or above the source width or `MaxWidth` are skipped. |
| `int MaxWidth`               | `1920`  | The widest variant. Every image also gets a variant at its own width, up to this value.                                             |
| `int Quality`                | `80`    | Lossy WebP quality from 1 to 100.                                                                                                   |

A custom `IImageProcessor` ignores these settings.

## `IImageProcessor`

Writes the variants of a local image. To replace the default WebP processor, implement the
interface and register it as a singleton:

```csharp
builder.Services.AddSingleton<IImageProcessor, MyImageProcessor>();
```

| Member                                                                                                                                | Behavior                                                                                                  |
| ------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| `Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory, CancellationToken cancellationToken = default)` | Writes variants of the source image into `outputDirectory` and describes them.                            |
| `string? CacheIdentity`                                                                                                               | Lets later publishes reuse generated images while it stays the same. `null`, the default, disables reuse. |

The processor must support concurrent calls and keep no page or content state. Change
`CacheIdentity` whenever the output would change, including settings and encoder versions.

## `ProcessedImageInfo`

Returned by `IImageProcessor.ProcessAsync`.

| Property                               | Behavior                                                                                 |
| -------------------------------------- | ---------------------------------------------------------------------------------------- |
| `int OriginalWidth`                    | Source width in pixels as displayed, after EXIF orientation. Used for the `<img>` size.  |
| `int OriginalHeight`                   | Source height in pixels as displayed, after EXIF orientation. Used for the `<img>` size. |
| `IReadOnlyList<ImageVariant> Variants` | The variants in ascending width. All go into `srcset`, and the last is also the `src`.   |

## `ImageVariant`

`ImageVariant(string FileName, int Width)` describes one variant. `FileName` is relative
to the `outputDirectory` passed to `ProcessAsync`, and `Width` is in pixels.
