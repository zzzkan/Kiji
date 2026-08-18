---
title: Markdown and images
description: Front matter, the Markdig pipeline, and page-bundle image optimization.
order: 30
---

## Front matter is yours

Kiji does not define a front matter schema. You declare a class, and YAML keys map onto
it with camelCase naming; unknown keys are ignored.

```csharp
public sealed class PostFrontMatter
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

```markdown
---
title: Hello
createdAt: 2026-01-01
tags: [dotnet, kiji]
---

Body text.
```

## Choosing the files

By default every `*.md` under the content directory belongs to the source. Point
`Directory` at a subdirectory to keep markdown with different front matter in separate
collections, and use `Where` to skip files:

```csharp
builder.AddMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.Slug,
    configure: options =>
    {
        options.Directory = "posts";                   // contents/posts/**/*.md
        options.Where = file => !file.FileNameWithoutExtension.StartsWith('_');
    });
```

Scoping also narrows what an index page depends on: a page that enumerates this
collection re-renders when something under `contents/posts/` changes, not when any
markdown anywhere changes.

## Rendering

`MarkdownContent<T>` gives you the parsed front matter immediately and the body on
demand:

```razor
@code {
    protected override async Task OnParametersSetAsync()
    {
        _post = Posts[ContentKey];
        _html = await _post.RenderAsync();
    }
}
```

```razor
<div>@((MarkupString)_html)</div>
```

`ContentKey` is the dictionary key the route mapping supplied. `MarkdownFileInfo.Slug` is
the usual thing to key on: the directory name for an `index.md`, otherwise the file name
without its extension. So `contents/posts/hello/index.md` and `contents/posts/hello.md`
are both the page `hello`.

The body goes through Markdig with the advanced extensions enabled, plus link hardening
that adds `target="_blank" rel="noopener noreferrer"` to external links. Customize the
pipeline, the deserializer, or add HTML post-processing when you register the source:

```csharp
builder.AddMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.Slug,
    configure: options => options.ConfigurePipeline(pipeline => pipeline.UseEmojiAndSmiley()));
```

## Page-bundle images

Put an image next to the markdown that uses it and reference it by name:

```markdown
![A description](photo.jpg)
```

Kiji encodes responsive WebP variants, writes them into the same output directory as the
page's `index.html`, and emits a `<picture>` element with a `srcset`.

The URLs it emits are document-relative — `./photo.jpg.<hash>.640w.webp` — which is why
they keep working no matter what path the site is published under. This is also why Kiji
never emits a `<base>` element: it would re-root exactly these URLs.

Encoded variants are cached under `.kiji/cache`, keyed by content hash, so an unchanged
image is never re-encoded even after you delete `dist/`. Image metadata (EXIF, IPTC, XMP)
is stripped on the way out.

A site-root reference like `![](/img/logo.png)` is left alone and served from `wwwroot/`
instead. Those are not base-path safe, so route them through `Site.Path` in markup rather
than markdown if you publish under a sub-path.

To replace the encoder entirely, register your own `IImageAssetProcessor` in
`builder.Services`.
