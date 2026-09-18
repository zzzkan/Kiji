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
    options =>
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

`ContentKey` is the opaque dictionary key the route mapping supplied. For Markdown content,
Kiji uses the source file's absolute `FileInfo.FullName` internally: it uniquely identifies
the dictionary entry without assigning it any URL meaning. `FileInfo` is the ordinary
`System.IO.FileInfo`; Kiji does not add a slug or logical content path to it.

Derive a URL slug only when mapping pages. A page-bundle site can use the containing
directory name for `index.md`, while a flat-file site can use the extensionless file name.

The body goes through Markdig with the advanced extensions enabled, plus link hardening
that adds `target="_blank" rel="noopener noreferrer"` to external links. Customize the
pipeline, the deserializer, or add HTML transformations when you register the source:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    options => options.ConfigureMarkdown(pipeline => pipeline.UseEmojiAndSmiley()));
```

Use `ConfigureMarkdown` to customize Markdown parsing and rendering, such as adding
syntax extensions or changing how a standalone link is rendered. `ConfigureYaml`
customizes front matter deserialization:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    options => options.ConfigureYaml(yaml => yaml.WithCaseInsensitivePropertyMatching()));
```

Use `AddHtmlPostProcessor` to modify the resulting HTML instead:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    options => options.AddHtmlPostProcessor(html => $"<div class=\"markdown-body\">{html}</div>"));
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
instead. Those are not base-path safe. In Razor markup, prepend
`Site.BaseUrl.AbsolutePath`; in Markdown, prefer a document-relative image beside its page.

To replace the encoder entirely, supply a factory for your `IImageProcessor`:

```csharp
app.UseImageProcessor(() => new CustomProcessor());
```

`IImageProcessor.ProcessAsync` receives the source path, page output directory, optional
cache directory, and cancellation token. It writes the variants and returns their file
names and widths in a `ProcessedImageInfo`. See [API reference](../api-reference/#image-processing)
for the complete contract.

Kiji calls the factory lazily and shares the processor for the site's lifetime. The
last registration wins; a null factory or result is rejected. `RunAsync` disposes the
processor on success, failure, and cancellation, including asynchronous disposal. Each
factory must create an instance owned by that site.

The processor must support concurrent calls and must not retain page or content state.
Content changes and hot reload do not recreate it. Declare external encoder configuration
with `AddBuildInput`; if your processor caches variants, include encoder settings and
version in its cache identity. Changing captured settings requires recreating the site.
