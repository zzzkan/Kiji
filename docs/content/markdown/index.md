---
title: Markdown and images
description: Load Markdown with YAML front matter and publish responsive local images.
order: 40
---

## Register Markdown content

Markdown files can start with YAML front matter, whose fields you define in a class:

```csharp
public sealed class FrontMatter
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

```markdown
---
title: Hello, Kiji
description: The first post on the site.
publishedAt: 2026-01-01
tags: [dotnet, kiji]
---

This is a Markdown-backed page.
```

```csharp
builder.AddMarkdownContent<FrontMatter>();
```

This registers `ContentCollection<MarkdownContent<FrontMatter>>`. It creates no pages by
itself; map items to pages with `AddPageParameters<TPage>` as in
[Getting started](../getting-started/#add-a-markdown-page).

Keys are camel case, such as `publishedAt` for `PublishedAt`, and match case-insensitively.
Unknown keys, malformed YAML, and values that do not fit your class fail the build.
Dates without an offset are read as UTC.

## Choose the files

By default, Kiji loads `*.md` files recursively from `content/`. Set `Directory` to use
another directory, absolute or relative to the project, and `FileFilter` to skip files:

```csharp
builder.AddMarkdownContent<FrontMatter>(options =>
{
    options.Directory = "content/posts";
    options.FileFilter = file => !file.Name.StartsWith('_');
});
```

When the content directory is inside the project, exclude it from the SDK's default
items, as in [Getting started](../getting-started/#create-the-project). Otherwise
every build and `dotnet watch` start enumerates every file in it. Kiji still loads and
watches the files, so edits, additions and deletions reload the development server.
The SDK then neither compiles C# or Razor files there nor shows them in Visual Studio's
Solution Explorer unless Show All Files is on, so keep components outside the directory:

```xml
<PropertyGroup>
  <DefaultItemExcludes>$(DefaultItemExcludes);content/**</DefaultItemExcludes>
</PropertyGroup>
```

The pattern is relative to the project, so `content/**` does not exclude `wwwroot/content/`.

## Use a site-specific model

To give pages a model with a slug or validated values, project each file:

```csharp
public sealed record Post(string Slug, string Title, MarkdownContent<FrontMatter> Content);
```

```csharp
builder.AddMarkdownContent<FrontMatter, Post>(content => new Post(
    Slug: Path.GetFileName(Path.GetDirectoryName(content.Path))!,
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

This registers `ContentCollection<Post>` instead. The projection sees one file at a time;
compute collection-wide results, such as tag counts, while rendering or in a scoped service.

## Register several collections of the same type

Use a service key for sources such as translations or documentation versions:

```csharp
builder.AddKeyedMarkdownContent<FrontMatter>("en", options => options.Directory = "content/en");
builder.AddKeyedMarkdownContent<FrontMatter>("ja", options => options.Directory = "content/ja");
var english = services.GetRequiredKeyedService<ContentCollection<MarkdownContent<FrontMatter>>>("en");
```

A key identifies the DI service; each item's ID is still its `Path` relative to that source.

## Render a Markdown body

Look up the item by the key passed through `AddPageParameters` and use `MarkdownBody`:

```razor
@inject ContentCollection<MarkdownContent<FrontMatter>> Posts

<div class="prose"><MarkdownBody Content="@Posts[ContentKey]" /></div>

@code {
    [Parameter] public string ContentKey { get; set; } = string.Empty;
}
```

With the `Post` model, use `Content="@Posts[ContentKey].Content"`. You can also call
`RenderAsync()` and render the result as a `MarkupString`. These produce raw HTML,
so render only trusted Markdown or sanitize it first.

## Customize parsing and HTML

`MarkdownOptions` applies to every source. Defaults include GFM-style tables, task lists,
autolinks and strikethrough, footnotes, GitHub heading IDs, and `{.class}` attributes.
Other Markdig extensions, such as math and diagrams, are opt-in:

```csharp
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.Serialization.NamingConventions;

builder.Services.Configure<MarkdownOptions>(options =>
{
    options.Pipeline.UseEmojiAndSmiley();
    options.Pipeline.Extensions.Replace<AutoIdentifierExtension>(new AutoIdentifierExtension(AutoIdentifierOptions.Default));
    options.Yaml.WithNamingConvention(UnderscoredNamingConvention.Instance);
});
```

`Use*` leaves an extension that is already registered unchanged, so use
`Extensions.Replace` to change its settings. To transform the parsed document, attach
`options.Pipeline.DocumentProcessed`; put HTML wrappers in Razor. To discard unknown
front matter keys, call `options.Yaml.IgnoreUnmatchedProperties()`.

## Links and deployment paths

Kiji does not add the base path to links starting with `/`, so prefer
document-relative links when the site is published under a
[subpath](../deployment/#publish-under-a-subpath).

## Local images

Place an image beside a Markdown file and reference it with a relative URL:

```text
content/
└── hello-kiji/
    ├── index.md
    └── photo.jpg
```

```markdown
![A description](photo.jpg)
```

For `.jpg`, `.jpeg`, `.png`, `.gif`, and `.webp` files, Kiji writes WebP versions beside
the page's `index.html`: 320, 640, 960, and 1280 pixels wide when the source is wider,
plus one at the source width, up to 1920 pixels. The `<img>` gets a `srcset` and the source
dimensions, and every image after the first loads lazily. To change the widths or the
quality, see [Image settings](../api-reference/#image-settings).

Attributes such as `![Photo](photo.jpg){width=300 loading=eager}` replace the ones Kiji
generates. When only `width` or `height` is a positive integer, Kiji computes the other from
the source aspect ratio: for a 1920×1080 source, `width=300` gives `height="169"`.

The output is static: animated images keep only their first frame, metadata such as EXIF
is removed, and colors are converted to sRGB. To keep an image as is, put it in `wwwroot/`
and reference it as a static asset. Remote images, paths starting with `/`, and `<img>`
written in HTML are left unchanged.

A missing image, or a path outside the Markdown file's directory tree, fails the render.
To replace the image processor, see [Image processing](../api-reference/#image-processing).
