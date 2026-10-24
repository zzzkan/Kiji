---
title: Markdown and images
description: Load Markdown with YAML front matter and publish responsive local images.
order: 40
---

Kiji can load Markdown files as typed content, render their bodies with Markdig, and
process images stored beside them. Your site remains in control of the content model and
the URLs it generates.

## Register Markdown content

Each Markdown file starts with YAML front matter. The fields are defined by your own .NET
class.

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

# Hello, Kiji

This is a Markdown-backed page.
```

Register the source:

```csharp
app.UseMarkdownContent<FrontMatter>();
```

This makes a `ContentDictionary<MarkdownContent<FrontMatter>>` available through
dependency injection. It does not automatically create routes; map the items with
`AddPageParameters<TPage>` as shown in [Getting started](../getting-started/#add-a-markdown-page).

Every Markdown file must begin with a front matter block delimited by `---` lines. Kiji
uses camel-case YAML property names and ignores unknown properties by default. A missing
or malformed block, invalid YAML, or a value that cannot be assigned to your model fails
the build.

## Choose the files

By default, Kiji recursively loads `*.md` files from `content/`. Set `Directory` to use a
different directory, and `FileFilter` to exclude files before they are loaded:

```csharp
app.UseMarkdownContent<FrontMatter>(options =>
{
    options.Directory = "content/posts";
    options.FileFilter = file => !file.Name.StartsWith('_');
});
```

`Directory` accepts an absolute path or a path relative to the site project directory.
Kiji watches each registered directory during development. File selection and page
parameters are separate: excluding a draft decides whether it is loaded, while
`AddPageParameters` decides the URL of every included item.

## Use a site-specific model

The basic overload exposes `MarkdownContent<TFrontMatter>` directly. Use the projection
overload instead when pages should consume a model that includes a slug or validated
display values. Define a model in its own `Post.cs` file, using your site's model
namespace:

```csharp
public sealed record Post(
    string Slug,
    string Title,
    MarkdownContent<FrontMatter> Content);
```

Replace the basic `UseMarkdownContent<FrontMatter>()` registration with this one.

```csharp
app.UseMarkdownContent<FrontMatter, Post>(content => new Post(
    Slug: content.FileInfo.Directory!.Name,
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

The projection runs independently for each file. Build shared tag indexes or other
collection-wide results with [UseContentSource](../api-reference/#content-sources-and-contentdictionary).
See [Incremental builds](../incremental-builds/#render-pages-that-use-external-data)
when a projection reads additional external data.

## Render a Markdown body

With the basic `UseMarkdownContent<FrontMatter>()` registration, inject the
dictionary, look up the item by the `ContentKey` supplied through `AddPageParameters`, and call
`RenderAsync()`:

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

With the projected `Post` model above, inject `ContentDictionary<Post>` and render
`await Posts[ContentKey].Content.RenderAsync()`.

The returned string contains HTML and must be rendered as `MarkupString`. Only render
trusted Markdown, or add your own sanitization step before displaying it.

## Customize parsing and HTML

Kiji enables Markdig's advanced extensions. Add another Markdig configuration with
`ConfigureMarkdown`:

```csharp
app.UseMarkdownContent<FrontMatter>(options =>
    options.ConfigureMarkdown(pipeline => pipeline.UseEmojiAndSmiley()));
```

Use `ConfigureYaml` to change front matter deserialization:

```csharp
app.UseMarkdownContent<FrontMatter>(options =>
    options.ConfigureYaml(yaml => yaml.WithCaseInsensitivePropertyMatching()));
```

Use `AddHtmlPostProcessor` for a synchronous transformation of the rendered HTML:

```csharp
app.UseMarkdownContent<FrontMatter>(options =>
    options.AddHtmlPostProcessor(html => $"<div class=\"prose\">{html}</div>"));
```

Multiple configurations and post-processors run in registration order.

## Links and deployment paths

External links receive `target="_blank" rel="noopener noreferrer"`. Other links are
rendered as written. Kiji does not add `SiteInfo.BaseUrl` to root-relative Markdown links.

A link such as `/about/` therefore points at the domain root and is wrong when the site is
published under `/my-site/`. Prefer document-relative links in Markdown. In Razor, resolve
site links with `Site.ResolveUrl("about/").AbsolutePath`. See [Deployment](../deployment/) for the full
subpath setup.

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

For relative `.jpg`, `.jpeg`, `.png`, `.gif`, and `.webp` references, Kiji writes WebP
versions of the image beside the page's `index.html` and emits an `<img>` with a `srcset`.
The default processor creates versions 320, 640, 960, and 1280 pixels wide when the source
is wider, plus one at the source width, up to 1920 pixels. It outputs static images:
animated GIF and WebP inputs use only their first frame. Source EXIF, IPTC, and XMP
metadata is removed, and colors are converted to sRGB. To preserve animation, place the
original image in `wwwroot/` and reference it as a static asset.

The `<img>` includes the source width and height to reserve layout space. The first image
in each Markdown body loads eagerly; later images load lazily and let the browser choose
a version from their rendered width. For finer control, write the `<img>` element in HTML
or Razor yourself; Kiji leaves explicit markup unchanged.

The source image must exist inside the Markdown file's directory tree. A missing image or
a path that escapes that tree fails the page render.

External image URLs and site-root references such as `![](/img/logo.png)` are left as
ordinary Markdown images. Put shared images in `wwwroot/`; remember that a leading `/` is
not safe for a deployment subpath.

For custom image processing, see the [API reference](../api-reference/#image-processing).
