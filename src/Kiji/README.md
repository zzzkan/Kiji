# Kiji

A static site generator for .NET. Write pages as Razor components, ship static HTML. Markdown with your own YAML front matter shape, responsive WebP image optimization, RSS feeds, sitemaps, and a live-reloading dev server are all in this one package.

## Quick start

Requires the .NET 10 SDK.

```powershell
dotnet new console -f net10.0 -o MySite
cd MySite
dotnet add package Kiji --version 0.1.0-preview
```

Use the [getting-started guide](https://zzzkan.github.io/kiji/docs/getting-started/) for project configuration, pages, and the document shell. Then `dotnet watch` serves the site at <http://localhost:8080> and `dotnet publish -c Release -o dist` generates the static output.

## Documentation

<https://zzzkan.github.io/kiji/>

## License

Kiji is MIT licensed.

Dependencies retain their own licenses; ImageSharp's [license](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE) grants Apache-2.0 use for open-source projects and transitive consumers. Direct use in other projects must meet its stated conditions.
