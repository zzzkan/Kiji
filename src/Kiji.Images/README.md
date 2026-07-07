# Kiji.Images

Responsive image optimization for the [Kiji](https://github.com/zzzkan/kiji)
static site generator: WebP variants with `srcset` support, powered by
SixLabors.ImageSharp.

## Install

```powershell
dotnet add package Kiji.Images
```

## Getting started

```csharp
using Kiji.Images;

builder.AddImageOptimization();

// Or with options:
builder.AddImageOptimization(options => options.Quality = 80);
```

With the backend registered, markdown images are optimized into responsive
WebP variants and rendered with `srcset`, `sizes`, lazy loading, and intrinsic
dimensions (CLS optimization).

## Third-party license note

This package depends on [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp),
which is distributed under the
[Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE):
Apache 2.0 terms apply when ImageSharp is consumed by open-source projects, as a
transitive package dependency (which is how this package delivers it), by
non-profits, or by companies under 1M USD annual gross revenue; other direct
commercial use requires a commercial license from Six Labors. Review the license
to confirm which terms apply to your usage.
