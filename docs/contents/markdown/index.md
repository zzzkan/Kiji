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
class:

```csharp
public sealed class PostFrontMatter
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
app.UseMarkdownContent<PostFrontMatter>();
```

This makes a `ContentDictionary<MarkdownContent<PostFrontMatter>>` available through
dependency injection. It does not automatically create routes; map the items with
`AddPages<TPage>` as shown in [Getting started](../getting-started/#add-a-markdown-page).

Every Markdown file must begin with a front matter block delimited by `---` lines. Kiji
uses camel-case YAML property names and ignores unknown properties by default. A missing
or malformed block, invalid YAML, or a value that cannot be assigned to your model fails
the build.

## Choose the files

By default, Kiji recursively loads `*.md` files from `contents/`. Set `Directory` to use a
subdirectory, and `FileFilter` to exclude files before they are loaded:

```csharp
app.UseMarkdownContent<PostFrontMatter>(options =>
{
    options.Directory = "posts";
    options.FileFilter = file => !file.Name.StartsWith('_');
});
```

`Directory` is relative to the configured content directory. File selection and URL
mapping are separate: excluding a draft decides whether it is loaded, while `AddPages`
decides the URL of every included item.

## Use a site-specific model

The basic overload exposes `MarkdownContent<TFrontMatter>` directly. Use the projection
overload when pages should consume a model that includes a slug or validated display
values:

```csharp
app.UseMarkdownContent<PostFrontMatter, Post>(content => new Post(
    Slug: Path.GetFileNameWithoutExtension(content.FileInfo.Name),
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

The projection runs independently for each file. Keep relationships such as tag indexes
or related posts outside the projection so they can be built from the resulting
collection.

## Render a Markdown body

Inject the registered dictionary, look up the item by the `ContentKey` supplied through
`AddPages`, and call `RenderAsync()`:

```razor
@inject ContentDictionary<MarkdownContent<PostFrontMatter>> Posts

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

The returned string contains HTML and must be rendered as `MarkupString`. Only render
trusted Markdown, or add your own sanitization step before displaying it.

## Customize parsing and HTML

Kiji enables Markdig's advanced extensions. Add another Markdig configuration with
`ConfigureMarkdown`:

```csharp
app.UseMarkdownContent<PostFrontMatter>(options =>
    options.ConfigureMarkdown(pipeline => pipeline.UseEmojiAndSmiley()));
```

Use `ConfigureYaml` to change front matter deserialization:

```csharp
app.UseMarkdownContent<PostFrontMatter>(options =>
    options.ConfigureYaml(yaml => yaml.WithCaseInsensitivePropertyMatching()));
```

Use `AddHtmlPostProcessor` for a synchronous transformation of the rendered HTML:

```csharp
app.UseMarkdownContent<PostFrontMatter>(options =>
    options.AddHtmlPostProcessor(html => $"<div class=\"prose\">{html}</div>"));
```

Multiple configurations and post-processors run in registration order.

## Links and deployment paths

External links receive `target="_blank" rel="noopener noreferrer"`. Other links are
rendered as written. Kiji does not add `SiteInfo.BaseUrl` to root-relative Markdown links.

A link such as `/about/` therefore points at the domain root and is wrong when the site is
published under `/my-site/`. Prefer document-relative links in Markdown. In Razor, prefix
site links with `Site.BaseUrl.AbsolutePath`. See [Deployment](../deployment/) for the full
sub-path setup.

## Local images and page bundles

Place an image beside a Markdown file and reference it with a relative URL:

```text
contents/
└── hello-kiji/
    ├── index.md
    └── photo.jpg
```

```markdown
![A description](photo.jpg)
```

For relative `.jpg`, `.jpeg`, `.png`, `.gif`, and `.webp` references, Kiji generates
responsive WebP variants beside the page's `index.html` and emits an `<img>` with a
`srcset`.

The source image must exist inside the Markdown file's directory tree. A missing image or
a path that escapes that tree fails the page render.

External image URLs and site-root references such as `![](/img/logo.png)` are left as
ordinary Markdown images. Put shared images in `wwwroot/`; remember that a leading `/` is
not safe for a deployment sub-path.

To replace responsive image generation, register an `IImageProcessor`. The complete
ownership, concurrency, and return-value contract is in the
[API reference](../api-reference/#image-processing).
