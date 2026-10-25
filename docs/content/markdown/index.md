---
title: Markdown and images
description: Load Markdown with YAML front matter and publish responsive local images.
order: 40
---

## Register Markdown content

Each Markdown file starts with YAML front matter, whose fields you define in a class:

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

Front matter keys are camel case, such as `publishedAt` for `PublishedAt`, and unknown keys
are ignored. A missing or malformed front matter block, or a value that does not fit your
class, fails the build.

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
    Slug: content.FileInfo.Directory!.Name,
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

This registers `ContentDictionary<Post>` instead. The projection sees one file at a time;
build collection-wide results, such as tag counts, with
[AddContentSource](../api-reference/#content-sources-and-contentdictionary).

## Render a Markdown body

Look up the item by the key passed through `AddPageParameters` and call `RenderAsync()`:

```razor
@inject ContentDictionary<MarkdownContent<FrontMatter>> Posts

<div>@((MarkupString)_html)</div>

@code {
    [Parameter] public string ContentKey { get; set; } = string.Empty;
    private string _html = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _html = await Posts[ContentKey].RenderAsync();
    }
}
```

With the `Post` model, call `Posts[ContentKey].Content.RenderAsync()`. The result is
raw HTML, so render only trusted Markdown or sanitize it first.

## Customize parsing and HTML

Kiji uses Markdig with its advanced extensions. Options add Markdig configuration,
YamlDotNet configuration, and HTML post-processing, each run in registration order:

```csharp
builder.AddMarkdownContent<FrontMatter>(options =>
{
    options.ConfigureMarkdown(pipeline => pipeline.UseEmojiAndSmiley());
    options.ConfigureYaml(yaml => yaml.WithCaseInsensitivePropertyMatching());
    options.AddHtmlPostProcessor(html => $"<div class=\"prose\">{html}</div>");
});
```

## Links and deployment paths

External links get `target="_blank" rel="noopener noreferrer"`; other links are written
as is. Kiji does not add the base path to links starting with `/`, so prefer
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
dimensions, and every image after the first loads lazily.

The output is static: animated images keep only their first frame, metadata such as EXIF
is removed, and colors are converted to sRGB. To keep an image as is, put it in `wwwroot/`
and reference it as a static asset. Remote images, paths starting with `/`, and `<img>`
written in HTML are left unchanged.

A missing image, or a path outside the Markdown file's directory tree, fails the render.
To replace the image processor, see [Image processing](../api-reference/#image-processing).
