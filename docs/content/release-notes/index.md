---
title: Release notes
description: Markdown API changes and migration steps for existing sites.
order: 100
---

## Unreleased: Markdown API

Markdown file selection now uses `MarkdownContentOptions` in `AddMarkdownContent`.
Move `ConfigureMarkdown` and `ConfigureYaml` callbacks to
`builder.Services.Configure<MarkdownOptions>`, using `options.Pipeline` and
`options.Yaml`. Settings are shared across sources. Restart `dotnet watch` after
changing this configuration; Hot Reload and content reloads do not reapply it.

Use `AddKeyedMarkdownContent<TFrontMatter>(key, ...)` or its projection overload for
keyed DI. Resolve it with `GetRequiredKeyedService<ContentDictionary<T>>(key)` or
`[Inject(Key = "posts")]` on a property inside Razor's `@code` block. The `@inject`
directive does not support keys. See [Markdown and images](../markdown/#use-a-site-specific-model).

`AddHtmlPostProcessor` has been removed. Compose HTML wrappers in Razor; use
`options.Pipeline.DocumentProcessed` for Markdown AST transformations. External links
now retain normal Markdig output and open in the current tab.

## Enable optional Markdown syntax

The default pipeline includes tables, task lists, autolinks, strikethrough, footnotes,
GitHub heading IDs and generic attributes. Definition lists, math, `:::` containers,
`==marked text==`, `^superscript^` and other advanced extensions are now opt-in.
Without an extension, its syntax can appear literally in published HTML.

To enable the previous advanced syntax:

```csharp
using Kiji.Markdown;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Microsoft.Extensions.DependencyInjection;

builder.Services.Configure<MarkdownOptions>(options =>
{
    options.Pipeline.UseAdvancedExtensions();
    options.Pipeline.Extensions.Replace<EmphasisExtraExtension>(
        new EmphasisExtraExtension(EmphasisExtraOptions.Default));
});
```

`UseAdvancedExtensions()` alone keeps the existing strikethrough extension, which
derives from `EmphasisExtraExtension`. The explicit replacement also enables marked
text, superscript and subscript; a single `~text~` then becomes subscript instead of
strikethrough. Math output still needs your site's math rendering setup.

## Unknown front matter keys now fail the build

Unknown keys are errors by default, with the source file and key in the message.
Imported articles containing fields such as Hugo's `draft` or `aliases` must add
corresponding model properties, remove those keys, or explicitly discard unknown keys:

```csharp
builder.Services.Configure<MarkdownOptions>(options =>
    options.Yaml.IgnoreUnmatchedProperties());
```

Discarded keys are unavailable to pages. Missing, empty and comment-only front matter
blocks create the model with its defaults.

## Heading anchors have changed

Heading IDs now follow GitHub's algorithm, including Unicode headings. Punctuation can
change IDs even for ASCII headings, breaking incoming links with the previous anchors:

| Heading | Previous ID | New ID |
| --- | --- | --- |
| `A / B` | `a-b` | `a--b` |
| `Foo - Bar` | `foo-bar` | `foo---bar` |
| `C# & .NET 10` | `c.net-10` | `c--net-10` |

Update incoming links or retain individual anchors with explicit IDs, for example
`## A / B {#a-b}`. To keep the previous heading algorithm site-wide:

```csharp
using Markdig.Extensions.AutoIdentifiers;

builder.Services.Configure<MarkdownOptions>(options =>
    options.Pipeline.Extensions.Replace<AutoIdentifierExtension>(
        new AutoIdentifierExtension(AutoIdentifierOptions.Default)));
```

Public API compatibility checks cannot detect changed HTML anchors. The documentation's
existing internal links are unaffected by this heading change.
