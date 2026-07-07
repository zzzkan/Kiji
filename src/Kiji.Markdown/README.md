# Kiji.Markdown

Markdown content support for the [Kiji](https://github.com/zzzkan/kiji) static
site generator: YAML front matter parsing (user-definable schema), Markdig
rendering through a shared pipeline, secure external links, and responsive
image markup when an image backend is registered.

## Install

```powershell
dotnet add package Kiji.Markdown
```

## Getting started

```csharp
using Kiji;
using Kiji.Markdown;

var builder = KijiApp.CreateBuilder(args);
builder.Site = new SiteInfo { BaseUrl = new Uri("https://example.com"), Name = "My Site" };

// Front matter deserializes into your own type.
var posts = builder.AddMarkdownContent<FrontMatter>()
    .WithKey(post => post.FileInfo.FileNameWithoutExtension)
    .OrderByDescending(post => post.FrontMatter.CreatedAt);
```

## Customization

```csharp
builder.AddMarkdownContent<FrontMatter>(options => options
    // Full access to the Markdig pipeline (add or remove extensions).
    .ConfigurePipeline(b => b.UseEmojiAndSmiley())
    // Transform the final HTML of each file, e.g. add heading anchor links.
    .AddHtmlPostProcessor(html => MyRegexes.HeadingAnchor().Replace(
        html, """<h$1 id="$2"><a class="anchor" href="#$2">#</a>"""))
    // Adjust YAML front matter deserialization.
    .ConfigureFrontMatter(b => b.WithNamingConvention(UnderscoredNamingConvention.Instance)));
```

The built-in `SecureLinkExtension` (adds `target="_blank" rel="noopener noreferrer"`
to external links) can be removed with
`options.ConfigurePipeline(b => b.Extensions.TryRemove<SecureLinkExtension>())`.

Responsive image markup and WebP optimization activate automatically when an
image backend such as `Kiji.Images` is registered.
