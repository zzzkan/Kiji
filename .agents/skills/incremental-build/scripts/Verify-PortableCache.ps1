param()
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$runRoot = Join-Path $repository ('artifacts/portable-cache-check/' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $runRoot 'feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
& dotnet pack (Join-Path $repository 'src/Kiji') -c Release -o $feed
if ($LASTEXITCODE -ne 0) { throw 'Package validation failed.' }
$package = Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Select-Object -First 1
$version = $package.BaseName.Substring('Kiji.'.Length)
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    if ($archive.Entries.FullName -contains 'build/Kiji.Inputs.targets') { throw 'Package still contains compiler-input targets.' }
} finally { $archive.Dispose() }

# Isolate the consuming projects from this repository's build configuration.
'<Project />' | Set-Content (Join-Path $runRoot 'Directory.Build.targets')
'<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' | Set-Content (Join-Path $runRoot 'Directory.Build.props')
'<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' | Set-Content (Join-Path $runRoot 'Directory.Packages.props')
@"
<configuration><packageSources><clear /><add key="local" value="$feed" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content (Join-Path $runRoot 'NuGet.Config')

function New-Fixture([string] $name) {
    $root = Join-Path $runRoot $name
    New-Item -ItemType Directory -Path (Join-Path $root 'contents') -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup><OutputType>Exe</OutputType><RootNamespace>Portable</RootNamespace></PropertyGroup>
  <ItemGroup><PackageReference Include="Kiji" Version="$version" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $root 'Portable.csproj')
    @'
using Kiji;
using Kiji.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Portable;
var site = StaticSite.Create(args);
site.Info = new SiteInfo { Name = "Portable", BaseUrl = new Uri("https://example.test/") };
site.UseMarkdownContent<FrontMatter>(options => options.AddHtmlPostProcessor(html => { Console.WriteLine("RENDER"); return html; }));
site.AddPages<Article>(services => services.GetRequiredService<ContentDictionary<MarkdownContent<FrontMatter>>>().Select(entry => new { Key = entry.Key }));
await site.RunAsync();
namespace Portable { public sealed class FrontMatter { public string? Title { get; set; } } }
'@ | Set-Content (Join-Path $root 'Program.cs')
    @'
@page "/{Key}/"
@using Kiji
@using Kiji.Markdown
@inject ContentDictionary<MarkdownContent<FrontMatter>> Articles
@((Microsoft.AspNetCore.Components.MarkupString)body)
<small>@SourcePath()</small>
@code {
    [Microsoft.AspNetCore.Components.Parameter] public string Key { get; set; } = "";
    private string body = "";
    protected override async Task OnInitializedAsync() { body = await Articles[Key].RenderAsync(); }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;
}
'@ | Set-Content (Join-Path $root 'Article.razor')
    "---`ntitle: First`n---`n`nStable body.`n`n![image](source.png)" | Set-Content (Join-Path $root 'contents/first.md')
    "---`ntitle: Second`n---`n`nSecond body." | Set-Content (Join-Path $root 'contents/second.md')
    [IO.File]::WriteAllBytes((Join-Path $root 'contents/source.png'), [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR4nGP4z8DwH4QZYAwAR8oH+WdZbrcAAAAASUVORK5CYII='))
    return $root
}

function Publish-Fixture([string] $root, [string] $label, [int] $expectedRenders, [string[]] $extra = @()) {
    $output = @(& dotnet publish $root -c Release -o (Join-Path $root 'dist') -p:KijiVerbose=true "-p:SourceRevisionId=$label" "-p:RestorePackagesPath=$runRoot/packages" @extra 2>&1)
    $output | Set-Content (Join-Path $runRoot ($label + '.log'))
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $label. See $runRoot." }
    $renders = @($output | Where-Object { "$_".Trim() -eq 'RENDER' }).Count
    if ($renders -ne $expectedRenders) { throw "$label rendered $renders pages; expected $expectedRenders. See $runRoot." }
    $manifest = Get-Content -LiteralPath (Join-Path $root '.kiji/cache/manifest.json') -Raw | ConvertFrom-Json
    if (@($manifest.CodeDependencies | Where-Object { $_ -notmatch ':mvid:[0-9a-f]{32}$' }).Count) {
        throw "Unexpected code identity: $label."
    }
    foreach ($folder in @('bin', 'obj')) {
        if (Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | Where-Object { $_.Name -like '*.kiji-inputs' -or $_.Name -eq 'kiji-inputs.stamp' }) {
            throw "Retired compiler-input artifacts found: $label."
        }
    }
}

function Clear-FixtureOutputs([string] $root) {
    $resolvedRoot = [IO.Path]::GetFullPath($root)
    foreach ($name in @('bin', 'obj', 'dist')) {
        $target = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $name))
        if (-not $target.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar)) { throw 'Cleanup escaped fixture.' }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
}

function Assert-SameOutputs([string] $first, [string] $second) {
    $left = @(Get-ChildItem -LiteralPath (Join-Path $first 'dist') -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath((Join-Path $first 'dist'), $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    } | Sort-Object)
    $right = @(Get-ChildItem -LiteralPath (Join-Path $second 'dist') -Recurse -File | ForEach-Object {
        [IO.Path]::GetRelativePath((Join-Path $second 'dist'), $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    } | Sort-Object)
    if (Compare-Object $left $right) { throw 'Incremental and full outputs differ.' }
}

$first = New-Fixture 'first'
$second = New-Fixture 'second'
Publish-Fixture $first 'initial' 2
if (-not ([IO.File]::ReadAllText((Join-Path $first 'dist/first.md/index.html')).Contains('/_/source/Article.razor'))) {
    throw 'CallerFilePath was not normalized by PathMap.'
}
Copy-Item -LiteralPath (Join-Path $first '.kiji') -Destination (Join-Path $second '.kiji') -Recurse
Publish-Fixture $second 'different-revision-and-checkout' 0
Assert-SameOutputs $first $second

$post = Join-Path $second 'contents/first.md'
$stamp = [IO.File]::GetLastWriteTimeUtc($post)
[IO.File]::WriteAllText($post, [IO.File]::ReadAllText($post).Replace('Stable', 'Edited'))
[IO.File]::SetLastWriteTimeUtc($post, $stamp)
Publish-Fixture $second 'content-only' 1

$page = Join-Path $second 'Article.razor'
# MSBuild owns source recompilation; unlike content inputs, source edits use normal timestamps.
[IO.File]::WriteAllText($page, [IO.File]::ReadAllText($page).Replace('@((Microsoft.AspNetCore.Components.MarkupString)body)', '<p>Changed code</p>@((Microsoft.AspNetCore.Components.MarkupString)body)'))
Publish-Fixture $second 'code-change' 2

$cache = Join-Path $second '.kiji/cache'
$manifest = Get-Content -LiteralPath (Join-Path $cache 'manifest.json') -Raw | ConvertFrom-Json
$image = $manifest.Pages.AdditionalOutputs | Select-Object -First 1
'broken' | Set-Content (Join-Path $cache ('images/' + $image.Hash))
Clear-FixtureOutputs $second
Publish-Fixture $second 'image-repair' 0

$manifest = Get-Content -LiteralPath (Join-Path $cache 'manifest.json') -Raw | ConvertFrom-Json
$bundlePath = Join-Path $cache $manifest.HtmlFile
$bundle = [IO.File]::ReadAllBytes($bundlePath)
$bundle[$manifest.Pages[0].HtmlOffset] = $bundle[$manifest.Pages[0].HtmlOffset] -bxor 1
[IO.File]::WriteAllBytes($bundlePath, $bundle)
Clear-FixtureOutputs $second
Publish-Fixture $second 'page-repair' 1

$full = New-Fixture 'full'
Copy-Item -LiteralPath $post -Destination (Join-Path $full 'contents/first.md') -Force
Copy-Item -LiteralPath $page -Destination (Join-Path $full 'Article.razor') -Force
Publish-Fixture $full 'full-comparison' 2
Assert-SameOutputs $full $second

# Exercise the SDK's repository-wide CI PathMap, with the project below the root.
# These repositories are local fixtures; the remote supplies Source Link metadata only.
$ciFirst = New-Fixture 'ci-first/site'
$ciRepository = Split-Path -Parent $ciFirst
& git -C $ciRepository init --quiet
& git -C $ciRepository config core.autocrlf false
& git -C $ciRepository remote add origin https://github.com/example/kiji-cache-fixture.git
& git -C $ciRepository add -- site
& git -C $ciRepository -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'CI fixture'
if ($LASTEXITCODE) { throw 'CI fixture commit failed.' }
$ciCheckout = Join-Path $runRoot 'ci-second'
& git -c core.autocrlf=false clone --quiet --local $ciRepository $ciCheckout
if ($LASTEXITCODE) { throw 'CI fixture clone failed.' }
& git -C $ciCheckout remote set-url origin https://github.com/example/kiji-cache-fixture.git
$ciSecond = Join-Path $ciCheckout 'site'
$ciSettings = @('-p:ContinuousIntegrationBuild=true')
Publish-Fixture $ciFirst 'ci-first' 2 $ciSettings
$ciHtml = [IO.File]::ReadAllText((Join-Path $ciFirst 'dist/first.md/index.html'))
if (-not $ciHtml.Contains('/_/source/Article.razor')) { throw 'Kiji project PathMap changed unexpectedly.' }
Copy-Item -LiteralPath (Join-Path $ciFirst '.kiji') -Destination (Join-Path $ciSecond '.kiji') -Recurse
Publish-Fixture $ciSecond 'ci-checkout' 0 $ciSettings
Assert-SameOutputs $ciFirst $ciSecond

$ciPost = Join-Path $ciSecond 'contents/first.md'
[IO.File]::WriteAllText($ciPost, [IO.File]::ReadAllText($ciPost).Replace('Stable', 'Edited'))
& git -C $ciCheckout add -- site/contents
& git -C $ciCheckout -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'Content only'
if ($LASTEXITCODE) { throw 'CI content commit failed.' }
Clear-FixtureOutputs $ciSecond
Publish-Fixture $ciSecond 'ci-content-commit' 1 $ciSettings

# Use the SDK's repository mapping alone so the project's position is observable.
@'
<Project>
  <Target Name="UseRepositoryPathMap" BeforeTargets="_SetPathMapFromSourceRoots">
    <PropertyGroup><PathMap /></PropertyGroup>
  </Target>
</Project>
'@ | Set-Content (Join-Path $ciSecond 'Directory.Build.targets')
Publish-Fixture $ciSecond 'ci-repository-map' 2 $ciSettings
$repositoryHtml = [IO.File]::ReadAllText((Join-Path $ciSecond 'dist/first.md/index.html'))
if (-not $repositoryHtml.Contains('/_/site/Article.razor')) { throw 'SDK repository PathMap was not exercised.' }

# Moving the project within its repository changes CallerFilePath and must invalidate.
$ciMoved = New-Fixture 'ci-second/moved'
Copy-Item -LiteralPath (Join-Path $ciSecond 'Directory.Build.targets') -Destination (Join-Path $ciMoved 'Directory.Build.targets')
Copy-Item -LiteralPath $ciPost -Destination (Join-Path $ciMoved 'contents/first.md') -Force
Copy-Item -LiteralPath (Join-Path $ciSecond '.kiji') -Destination (Join-Path $ciMoved '.kiji') -Recurse
Publish-Fixture $ciMoved 'ci-project-moved' 2 $ciSettings
$movedHtml = [IO.File]::ReadAllText((Join-Path $ciMoved 'dist/first.md/index.html'))
if (-not $movedHtml.Contains('/_/moved/Article.razor')) { throw 'Moved project reused stale source paths.' }

# A different virtual source prefix is also observable, even at the same physical path.
@'
<Project>
  <Target Name="RemapFixtureSource" BeforeTargets="_SetPathMapFromSourceRoots" DependsOnTargets="InitializeSourceRootMappedPaths">
    <PropertyGroup><PathMap /></PropertyGroup>
    <ItemGroup><SourceRoot Update="@(SourceRoot)"><MappedPath>/changed/</MappedPath></SourceRoot></ItemGroup>
  </Target>
</Project>
'@ | Set-Content (Join-Path $ciMoved 'Directory.Build.targets')
Publish-Fixture $ciMoved 'ci-mapping-changed' 2 $ciSettings
$remappedHtml = [IO.File]::ReadAllText((Join-Path $ciMoved 'dist/first.md/index.html'))
if (-not $remappedHtml.Contains('/changed/moved/Article.razor')) { throw 'Changed PathMap reused stale source paths.' }

# Ordinary C# and referenced library implementation changes must invalidate HTML too.
$codeSite = New-Fixture 'code-site'
$codePage = Join-Path $codeSite 'Article.razor'
[IO.File]::WriteAllText($codePage, [IO.File]::ReadAllText($codePage).Replace('<small>@SourcePath()</small>', '<small>@SourcePath()</small><p>@Label.Text</p>'))
$labelSource = Join-Path $codeSite 'Label.cs'
'namespace Portable; public static class Label { public static string Text => "First label"; }' | Set-Content $labelSource
Publish-Fixture $codeSite 'csharp-initial' 2
[IO.File]::WriteAllText($labelSource, [IO.File]::ReadAllText($labelSource).Replace('First label', 'Other label'))
Publish-Fixture $codeSite 'csharp-edited' 2
if (-not ([IO.File]::ReadAllText((Join-Path $codeSite 'dist/first.md/index.html')).Contains('Other label'))) { throw 'C# edit was not rendered.' }

$library = Join-Path $runRoot 'support'
New-Item -ItemType Directory -Path $library | Out-Null
'<Project Sdk="Microsoft.NET.Sdk" />' | Set-Content (Join-Path $library 'Support.csproj')
'namespace Support; public static class Label { public static string Text => "Library first"; }' | Set-Content (Join-Path $library 'Label.cs')
$project = Join-Path $codeSite 'Portable.csproj'
[IO.File]::WriteAllText($project, [IO.File]::ReadAllText($project).Replace('</Project>', '<ItemGroup><ProjectReference Include="../support/Support.csproj" /></ItemGroup></Project>'))
[IO.File]::WriteAllText($codePage, [IO.File]::ReadAllText($codePage).Replace('@Label.Text', '@Support.Label.Text'))
Publish-Fixture $codeSite 'reference-initial' 2
# Hold SourceRevisionId constant so only the implementation changes, not assembly attributes.
$librarySource = Join-Path $library 'Label.cs'
[IO.File]::WriteAllText($librarySource, [IO.File]::ReadAllText($librarySource).Replace('Library first', 'Library other'))
Publish-Fixture $codeSite 'reference-edited' 2 @('-p:SourceRevisionId=reference-initial')
if (-not ([IO.File]::ReadAllText((Join-Path $codeSite 'dist/first.md/index.html')).Contains('Library other'))) { throw 'Referenced implementation edit was not rendered.' }
Publish-Fixture $codeSite 'reference-unchanged' 0 @('-p:SourceRevisionId=reference-initial')

& (Join-Path $PSScriptRoot 'Verify-BuildModes.ps1') -RunRoot $runRoot -Site $codeSite -PackageVersion $version

# Development output must stay out of SDK compile globs and be removed by clean.
$devOutput = Join-Path $first '.kiji/dev-site'
New-Item -ItemType Directory -Path $devOutput -Force | Out-Null
'This is generated output, not C# source.' | Set-Content (Join-Path $devOutput 'ignored.cs')
Publish-Fixture $first 'dev-output-excluded' 0
& dotnet clean $first -c Release > (Join-Path $runRoot 'clean.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw "Clean failed. See $runRoot." }
if (Test-Path -LiteralPath (Join-Path $first '.kiji')) { throw 'Clean left Kiji cache or development output behind.' }
Write-Output "Portable cache verification passed. Logs and fixtures: $runRoot"

