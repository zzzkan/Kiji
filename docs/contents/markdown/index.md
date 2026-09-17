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
collections, and use `FileFilter` to skip files:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.FullName,
    configure: options =>
    {
        options.Directory = "posts";                   // contents/posts/**/*.md
        options.FileFilter = file => !Path.GetFileNameWithoutExtension(file.Name).StartsWith('_');
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

`ContentKey` is the dictionary key the route mapping supplied. For raw markdown content,
the source file's absolute `FileInfo.FullName` is a convenient key: it uniquely identifies
the dictionary entry without assigning it any URL meaning. `FileInfo` is the ordinary
`System.IO.FileInfo`; Kiji does not add a slug or logical content path to it.

Derive a URL slug only when mapping pages. A page-bundle site can use the containing
directory name for `index.md`, while a flat-file site can use the extensionless file name.

The body goes through Markdig with the advanced extensions enabled, plus link hardening
that adds `target="_blank" rel="noopener noreferrer"` to external links. Customize the
pipeline, the deserializer, or add HTML transformations when you register the source:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.FullName,
    configure: options => options.ConfigureMarkdig(pipeline => pipeline.UseEmojiAndSmiley()));
```

Use `ConfigureMarkdig` to customize Markdown parsing and rendering, such as adding
syntax extensions or changing how a standalone link is rendered. Use
`AddHtmlTransform` to modify the resulting HTML instead:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    key: post => post.FileInfo.FullName,
    configure: options => options.AddHtmlTransform(html => $"<div class=\"markdown-body\">{html}</div>"));
```

HTML transforms are synchronous and run in registration order, each receiving the
previous transform's output.

## Page-bundle images

Put an image next to the markdown that uses it and reference it by name:

```markdown
![A description](photo.jpg)
```

Kiji encodes responsive WebP variants, writes them into the same output directory as the
page's `index.html`, and emits an `<img>` element with a `srcset`.

The URLs it emits are document-relative — `./photo.jpg.<hash>.640w.webp` — which is why
they keep working no matter what path the site is published under. This is also why Kiji
never emits a `<base>` element: it would re-root exactly these URLs.

Encoded variants are cached under `.kiji/cache`. Unchanged images reuse them after you delete
`dist/`. Old cache variants remain available to other pages until `dotnet clean`;
publishing removes unreferenced variants from the site output. Image metadata
(EXIF, IPTC, XMP) is stripped on the way out.

Percent-encoded filenames such as `my%20photo.jpg` are supported. Query strings and
fragments on local image references are removed when resolving the source file.

A site-root reference like `![](/img/logo.png)` is left alone and served from `wwwroot/`
instead. Those are not base-path safe, so route them through `Site.Path` in markup rather
than markdown if you publish under a sub-path.

To replace the encoder entirely, supply a factory for your `IImageAssetProcessor`:

```csharp
app.UseImageAssetProcessor(() => new CustomProcessor());
```

Kiji calls the factory lazily and shares the processor for the site's lifetime. The
last registration wins; a null factory or result is rejected. Kiji disposes the processor
when the site is disposed, including asynchronous disposal. Each factory must create
an instance owned by that site.

The processor must support concurrent calls and must not retain page or content state.
Content changes and hot reload do not recreate it. Declare external encoder configuration
with `AddBuildInput`; if your processor caches variants, include encoder settings and
version in its cache identity. Changing captured settings requires recreating the site.
