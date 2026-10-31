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

This registers `ContentDictionary<MarkdownContent<FrontMatter>>`. It creates no pages by
itself; map items to pages with `AddPageParameters<TPage>` as in
[Getting started](../getting-started/#add-a-markdown-page).

Front matter is optional. A missing, empty or comment-only block creates your model with its defaults.
Keys use camelCase, such as `publishedAt`, with case-insensitive matching (`Tags` also
works). Unknown keys fail with a file and property name instead of silently losing data.
For snake_case keys, configure the YAML naming convention below or use `[YamlMember(Alias = "published_at")]`.
Malformed YAML and values that do not fit your class fail the build.
Dates without an offset use UTC; explicit offsets are preserved for `DateTimeOffset`.
`DateTime` values are normalized to UTC.

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
    Slug: content.Path.EndsWith("/index.md", StringComparison.OrdinalIgnoreCase)
        ? content.Path[..^"/index.md".Length].Replace('/', '-')
        : content.Path[..^".md".Length].Replace('/', '-'),
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

This registers `ContentDictionary<Post>` instead. The projection sees one file at a time;
build collection-wide results, such as tag counts, with
[AddContentSource](../api-reference/#content-sources-and-contentdictionary).

`content.Path` is relative to the registered content directory, includes `.md`, and uses
`/` separators; it is also the dictionary key. Use the whole path when deriving your own
slugs so repeated leaf directory names stay distinguishable. `FileInfo` is absolute and
should be used only for file access, never to derive portable output identities.

To register two sources with the same model, use keyed DI:

```csharp
builder.AddKeyedMarkdownContent<FrontMatter>("posts", options => options.Directory = "content/posts");
builder.AddKeyedMarkdownContent<FrontMatter>("notes", options => options.Directory = "content/notes");
var posts = services.GetRequiredKeyedService<ContentDictionary<MarkdownContent<FrontMatter>>>("posts");
```

In Razor, use the `Inject` attribute to specify the key:

```razor
@code {
    [Inject(Key = "posts")]
    public ContentDictionary<MarkdownContent<FrontMatter>> Posts { get; set; } = default!;
}
```

The `@inject` directive does not support service keys. `AddContentSource` registers
unkeyed dictionaries; its `sourceId` argument identifies dependencies, not a DI key.

## Render a Markdown body

Look up the item by the key passed through `AddPageParameters` and use `MarkdownBody`:

```razor
@inject ContentDictionary<MarkdownContent<FrontMatter>> Posts

<div class="prose"><MarkdownBody Content="@Posts[ContentKey]" /></div>

@code {
    [Parameter] public string ContentKey { get; set; } = string.Empty;
}
```

With the `Post` model, use `Content="@Posts[ContentKey].Content"`. You can also call
`RenderAsync()` and render the result as a `MarkupString`. These produce raw HTML,
so render only trusted Markdown or sanitize it first.

## Customize parsing and HTML

Configure `MarkdownOptions` through DI once for every source. The shared processor and
renderer pool survive content reloads. Defaults include GFM-style tables, task lists,
autolinks and strikethrough, footnotes, GitHub heading IDs (including Japanese), and `{.class}`
attributes. Media embeds, diagrams, citations and subscript are opt-in:

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

`Pipeline` and `Yaml` expose Markdig and YamlDotNet builders; customization follows those
packages' APIs and compatibility rules. `Use*` can leave an existing extension unchanged;
use `Extensions.Replace` or remove it first to change its settings. Kiji moves an enabled
GenericAttributes extension last before building. For AST transforms, attach
`options.Pipeline.DocumentProcessed`. Put presentation wrappers in Razor as above.
To intentionally discard unknown YAML keys, call `options.Yaml.IgnoreUnmatchedProperties()`.
Restart `dotnet watch` after changing `MarkdownOptions` configuration; these settings
are applied once and are not reapplied by Hot Reload or content reloads.

When upgrading, see the [Markdown release notes and migration guide](../release-notes/)
for optional syntax, strict YAML keys and changed heading anchors.

## Links and deployment paths

Links retain Markdig's normal output and attributes; external links use the current tab.
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

Explicit attributes such as `![Photo](photo.jpg){width=300 loading=eager}` override
Kiji's generated attributes without duplicates. If only `width` or `height` is a
positive integer, Kiji calculates the other dimension from the source aspect ratio,
rounding to the nearest pixel (minimum 1). For a 1920×1080 source, `width=300` produces
`width="300" height="169"`. Other values, such as `width=50%`, suppress Kiji's other
dimension. When both dimensions are supplied, Kiji preserves them as written.

A positive integer `width` also supplies a fixed `sizes` value, such as `sizes="300px"`,
unless you specify `sizes` yourself. Otherwise, `sizes` uses `auto` only when the
effective `loading` value is `lazy`.

The output is static: animated images keep only their first frame, metadata such as EXIF
is removed, and colors are converted to sRGB. To keep an image as is, put it in `wwwroot/`
and reference it as a static asset. Remote images, paths starting with `/`, and `<img>`
written in HTML are left unchanged.

A missing image, or a path outside the Markdown file's directory tree, fails the render.
To replace the image processor, see [Image processing](../api-reference/#image-processing).
