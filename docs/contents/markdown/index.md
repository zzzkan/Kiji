---
title: Markdown and images
description: Front matter, the Markdig pipeline, and page-bundle image optimization.
order: 30
---

## Included without extra setup

Register a Markdown content source and Kiji handles the common publishing workflow:
recursive file discovery, YAML front matter, Markdig rendering, local-image processing,
and output paths that remain valid below a deployment base path. Use the default behavior
first; the rest of this page shows the points where you can adapt it to your site.

## Basic usage

Create a Markdown file under `contents/`. A page bundle keeps the page and its local
images together:

```text
contents/
└── hello-world/
    └── index.md
```

Add front matter and body text to `index.md`:

```markdown
---
title: Hello, Kiji
description: The first post on your new site.
---

# Hello, Kiji

This is a Markdown-backed page.
```

Register the source and map each item to a page. The route value becomes part of the URL;
the additional `ContentKey` value identifies which Markdown item the page renders:

```csharp
app.UseMarkdownContent<PostFrontMatter>();

app.AddPages<PostPage>(services =>
    services.GetRequiredService<ContentDictionary<MarkdownContent<PostFrontMatter>>>()
        .Select(post => new
        {
            Slug = post.Value.FileInfo.Directory!.Name,
            ContentKey = post.Key,
        }));
```

The page component reads the item and renders its body:

```razor
@page "/posts/{Slug}/"
@inject ContentDictionary<MarkdownContent<PostFrontMatter>> Posts

@code {
    [Parameter] public string Slug { get; set; } = string.Empty;
    [Parameter] public string ContentKey { get; set; } = string.Empty;
    private string _html = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        _html = await Posts[ContentKey].RenderAsync();
    }
}

<div>@((MarkupString)_html)</div>
```

Run `dotnet watch` to preview the site or `dotnet publish -c Release -o dist` to generate
it. This is the basic flow: a Markdown source supplies content, `AddPages<TPage>` supplies
URLs, and a Razor component supplies the page shell.

This page-bundle example uses the directory containing `index.md` as the slug, so the file
above maps to `/posts/hello-world/`. A flat Markdown file can instead use its extensionless
filename. Both choices belong to the site's mapping code.

`UseMarkdownContent` registers a typed content dictionary; it does not turn every Markdown
file into a page without a page mapping. See [Routing](../concepts/#routing) for the
general page-mapping rules.

## Front matter and validation

Kiji does not define a front matter schema. You declare a class, and YAML keys map onto
it with camelCase naming; unknown keys are ignored by default.

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

Every Markdown file must start with a front matter block delimited by `---` lines. The
block itself is required, but Kiji does not require particular fields such as `Title` or
`CreatedAt`. A missing or malformed block, invalid YAML, or a value that cannot be
deserialized into your type fails content loading and therefore the build. Field-level
validation belongs in your model or in a projection step.

## Choosing the files

The source recursively scans the content directory for `*.md` files. Other extensions are
not part of the collection. Point `Directory` at a subdirectory to keep Markdown with
different front matter in separate collections, and use `FileFilter` to skip files before
they are loaded:

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

File selection is separate from URL mapping. Excluding `_draft.md` keeps it out of the
content dictionary; it does not decide what slug an included file receives.

## Raw content or a projection model

Use `MarkdownContent<TFrontMatter>` when the page needs the parsed front matter, source
metadata, and rendered body directly. Use the projection overload when pages should consume
a site model with values such as a slug, display title, or validated metadata:

```csharp
app.UseMarkdownContent<PostFrontMatter, Post>(content => new Post(
    Slug: Path.GetFileNameWithoutExtension(content.FileInfo.Name),
    Title: content.FrontMatter.Title ?? "Untitled",
    Content: content));
```

Each projection is evaluated independently for one Markdown file. A projection should not
depend on another content item; compute indexes, tags, or related content in the page that
enumerates the collection instead.

## Reading and rendering

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

`RenderAsync` is lazy: front matter is available when the collection is loaded, while the
Markdown body is converted to HTML only when a page renders it. It also materializes
variants for referenced local images beside the current page output, so Markdown containing
local images must be rendered within a page render.

When a page receives `ContentKey`, use `Posts[ContentKey]` for the lookup. Enumerating
`Posts.Values` to find the same item makes the page depend on the whole collection, which
causes it to re-render when any item changes.

The body goes through Markdig with the advanced extensions enabled, plus link hardening
that adds `target="_blank" rel="noopener noreferrer"` to external links. Customize the
pipeline, the deserializer, or add HTML transformations when you register the source:

```csharp
app.UseMarkdownContent<PostFrontMatter>(
    options => options.ConfigureMarkdown(pipeline => pipeline.UseEmojiAndSmiley()));
```

Use `ConfigureMarkdown` to customize Markdown parsing and rendering, such as adding
syntax extensions or changing how a standalone link is rendered. You keep the familiar
Markdig extension model rather than learning a Kiji-specific Markdown language.
`ConfigureYaml` customizes front matter deserialization:

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

Together, projection, Markdown configuration, YAML configuration, and HTML post-processors
let a site add its own content rules without replacing Kiji's content loading or publishing
pipeline.

## Links and deployment base paths

External Markdown links receive `target="_blank" rel="noopener noreferrer"`. Internal
Markdown links are otherwise rendered as written; Kiji does not automatically prepend
`Site.BaseUrl.AbsolutePath` to them.

A link such as `/about/` is therefore not base-path safe when the site is deployed under
`/my-site/`. Prefer document-relative links where appropriate, or construct site links with
the base path used by the deployment. See [Deployment](../deployment/) for the complete
base-path rules.

## Local images and page bundles

Put an image next to the markdown that uses it and reference it by name:

```markdown
![A description](photo.jpg)
```

Kiji encodes responsive WebP variants, writes them into the same output directory as the
page's `index.html`, and emits an `<img>` element with a `srcset`.

Automatic processing applies to relative image references with `.jpg`, `.jpeg`, `.png`,
`.gif`, or `.webp` extensions. External images such as `https://example.com/photo.png`,
site-root references such as `/img/logo.png`, and non-image files remain ordinary Markdown
images rather than becoming responsive page-bundle images.

A local image must exist and resolve inside the Markdown file's directory tree. Missing
images and references that escape that directory fail the page render.

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

## What changes rebuild

Incremental builds track what each page actually reads. Reading a Markdown item's front
matter or calling `RenderAsync` records that source Markdown file as a dependency. A page
that enumerates a content dictionary records the scoped content set, while a keyed lookup
such as `Posts[ContentKey]` can depend only on the corresponding Markdown file.

Local images add both the source image and the generated variants to the page's dependency
and output records. Editing a post therefore re-renders its detail page; editing or adding
a post also re-renders an index that enumerates the collection. A `Directory`-scoped source
keeps that index dependency limited to its directory rather than all Markdown content.

External files, remote-data versions, or custom image-encoder settings that Kiji cannot see
must be declared with `AddBuildInput`. See [Build inputs](../api-reference/#build-inputs)
for the registration forms and [Performance](../performance/) for the incremental-build
model.
