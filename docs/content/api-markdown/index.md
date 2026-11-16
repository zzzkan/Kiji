---
title: Kiji.Markdown namespace
navTitle: Kiji.Markdown
description: Markdown content registration, options, and rendering.
order: 120
section: API reference
---

See [Markdown and images](../markdown/) for examples.

## `MarkdownHostBuilderExtensions`

Each method loads the `*.md` files of one directory and registers a `ContentCollection<T>`:

| Method                                                                                                                                                                 | Registers                                                             |
| ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| `AddMarkdownContent<TFrontMatter>(Action<MarkdownContentOptions>? configure = null)`                                                                                   | `ContentCollection<MarkdownContent<TFrontMatter>>`                    |
| `AddMarkdownContent<TFrontMatter,TModel>(Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)`                         | `ContentCollection<TModel>`, converting each file with `select`       |
| `AddKeyedMarkdownContent<TFrontMatter>(string serviceKey, Action<MarkdownContentOptions>? configure = null)`                                                           | `ContentCollection<MarkdownContent<TFrontMatter>>` under `serviceKey` |
| `AddKeyedMarkdownContent<TFrontMatter,TModel>(string serviceKey, Func<MarkdownContent<TFrontMatter>,TModel> select, Action<MarkdownContentOptions>? configure = null)` | `ContentCollection<TModel>` under `serviceKey`                        |

Resolve a keyed collection with
`services.GetRequiredKeyedService<ContentCollection<T>>(serviceKey)`. Each element type can
be registered once per key.

## `MarkdownContentOptions`

Selects the files to load, through the `configure` callback.

| Property                          | Behavior                                                                                                |
| --------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `string Directory`                | Directory to load recursively, absolute or relative to the project. It must exist. Default `"content"`. |
| `Func<FileInfo,bool>? FileFilter` | Returns `false` to skip a file; called for one file at a time. Default: load every `*.md` file.         |

The directory may be outside the project or be a symbolic link. [Local images](../markdown/#local-images)
must stay inside a registered directory.

## `MarkdownOptions`

Parsing settings for all Markdown content. Configure them with
`builder.Services.Configure<MarkdownOptions>(options => ...)`.

| Property                           | Behavior                                                                                                                                               |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `MarkdownPipelineBuilder Pipeline` | Markdig builder. Defaults: GFM-style tables, task lists, autolinks and strikethrough; footnotes, GitHub heading IDs and generic attributes.            |
| `DeserializerBuilder Yaml`         | YamlDotNet builder for front matter. Defaults: camelCase keys matched case-insensitively, errors on unknown keys, and UTC for dates without an offset. |

## `MarkdownContent<TFrontMatter>`

One Markdown file in a collection.

| Member                                                                         | Behavior                                                                                                        |
| ------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------- |
| `string Path`                                                                  | The path relative to the content directory, using `/`. Also the collection ID.                                  |
| `TFrontMatter FrontMatter`                                                     | The parsed front matter.                                                                                        |
| `ValueTask<string> RenderAsync(CancellationToken cancellationToken = default)` | Renders the body to HTML. Call it while rendering a page, so Kiji can track the files and images the page uses. |

## `MarkdownBody<TFrontMatter>`

A component that renders a Markdown body with no wrapper element.

| Parameter                               | Behavior                         |
| --------------------------------------- | -------------------------------- |
| `MarkdownContent<TFrontMatter> Content` | Required. The content to render. |

```razor
<MarkdownBody Content="@content" />
```
