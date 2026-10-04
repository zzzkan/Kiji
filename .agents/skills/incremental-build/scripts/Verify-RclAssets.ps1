param([Parameter(Mandatory)][string] $RunRoot, [Parameter(Mandatory)][string] $PackageVersion)
$ErrorActionPreference = 'Stop'
$root = Join-Path $RunRoot 'rcl-assets'
foreach ($directory in @('site/wwwroot', 'library/wwwroot', 'leaf/wwwroot')) {
    New-Item -ItemType Directory -Force (Join-Path $root $directory) | Out-Null
}
$site = Join-Path $root 'site'
$project = Join-Path $site 'Consumer.csproj'
@"
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="Kiji" Version="$PackageVersion" /><ProjectReference Include="../library/Library.csproj" /></ItemGroup>
</Project>
"@ | Set-Content $project
@'
using Kiji;
var site = StaticSite.Create(args);
site.Info = new() { Name = "Consumer", BaseUrl = new Uri("https://example.test/sub/") };
site.AddStaticPages();
await site.RunAsync();
'@ | Set-Content (Join-Path $site 'Program.cs')
'@page "/"' + "`n<h1>Home</h1><Library.Card />" | Set-Content (Join-Path $site 'Home.razor')
'h1 { color: red; }' | Set-Content (Join-Path $site 'Home.razor.css')
'static before' | Set-Content (Join-Path $site 'wwwroot/raw.txt')
'<Project Sdk="Microsoft.NET.Sdk.Razor"><PropertyGroup><PackageId>Different.Library.Package</PackageId></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><ProjectReference Include="../leaf/Leaf.csproj" /></ItemGroup></Project>' | Set-Content (Join-Path $root 'library/Library.csproj')
'<p>Library</p><Leaf.LeafCard />' | Set-Content (Join-Path $root 'library/Card.razor')
'p { color: blue; }' | Set-Content (Join-Path $root 'library/Card.razor.css')
'library asset' | Set-Content (Join-Path $root 'library/wwwroot/library.txt')
'<Project Sdk="Microsoft.NET.Sdk.Razor"><PropertyGroup><PackageId>Different.Leaf.Package</PackageId><StaticWebAssetBasePath>vendor/leaf</StaticWebAssetBasePath></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup></Project>' | Set-Content (Join-Path $root 'leaf/Leaf.csproj')
'<span>Leaf</span>' | Set-Content (Join-Path $root 'leaf/LeafCard.razor')
'span { color: green; }' | Set-Content (Join-Path $root 'leaf/LeafCard.razor.css')
$leafText = 'leaf asset'
$leafText | Set-Content (Join-Path $root 'leaf/wwwroot/leaf.txt')
$dist = Join-Path $site 'dist'
function Invoke-RclBuild([string] $label, [string[]] $arguments) {
    & dotnet @arguments "-p:RestorePackagesPath=$RunRoot/packages" *> (Join-Path $RunRoot "$label.log")
    if ($LASTEXITCODE) { throw "RCL verification failed: $label. See $RunRoot." }
}
function Publish-Rcl([string] $label, [string] $color) {
    Invoke-RclBuild $label @('publish', $site, '-c', 'Release', '-o', $dist, '-p:KijiVerbose=true')
    $bundle = [IO.File]::ReadAllText((Join-Path $dist 'Consumer.styles.css'))
    if (!$bundle.Contains('color: red') -or !$bundle.Contains('_content/Different.Library.Package/') -or !$bundle.Contains('vendor/leaf/')) {
        throw "Missing local or referenced CSS: $label"
    }
    if ([IO.File]::ReadAllText((Join-Path $dist 'index.html')) -notmatch '<p b-[a-z0-9]+>Library</p><span b-[a-z0-9]+>Leaf</span>') {
        throw "Referenced component HTML lost CSS scope: $label"
    }
    if ([IO.File]::ReadAllText((Join-Path $dist 'vendor/leaf/leaf.txt')).Trim() -ne $leafText) {
        throw "Missing transitive asset at custom public path: $label"
    }
    $imports = @([regex]::Matches($bundle, "@import '(_content/Different.Library.Package/[^']+\.bundle\.scp\.css)';"))
    if ($imports.Count -ne 1) { throw "Missing or ambiguous RCL CSS import: $label" }
    $referencedCss = Join-Path $dist $imports[0].Groups[1].Value
    $stableCss = Join-Path $dist '_content/Different.Library.Package/Different.Library.Package.bundle.scp.css'
    $css = @(Get-ChildItem (Join-Path $dist '_content/Different.Library.Package') -Filter '*.bundle.scp.css')
    if ($referencedCss -eq $stableCss -or !(Test-Path -LiteralPath $referencedCss) -or
        !([IO.File]::ReadAllText($referencedCss)).Contains("color: $color") -or
        $css.Count -ne 2 -or !(Test-Path -LiteralPath $stableCss) -or
        (Get-FileHash -LiteralPath $referencedCss).Hash -ne (Get-FileHash -LiteralPath $stableCss).Hash) {
        throw "Missing, stale, or retired RCL CSS or URL alias: $label"
    }
    if ((Test-Path (Join-Path $dist 'wwwroot')) -or (Get-ChildItem $dist -Filter '*.json')) {
        throw "SDK publish byproducts leaked into site: $label"
    }
    return $referencedCss
}
$oldCss = Publish-Rcl 'rcl-project-initial' 'blue'
if (!(Test-Path (Join-Path $dist '_content/Different.Library.Package/library.txt'))) { throw 'Missing direct RCL asset.' }
$htmlStamp = [IO.File]::GetLastWriteTimeUtc((Join-Path $dist 'index.html'))
'p { color: cyan; }' | Set-Content (Join-Path $root 'library/Card.razor.css')
Remove-Item -LiteralPath (Join-Path $root 'library/wwwroot/library.txt')
$raw = Join-Path $site 'wwwroot/raw.txt'
$rawStamp = [IO.File]::GetLastWriteTimeUtc($raw)
[IO.File]::WriteAllText($raw, 'static after!')
[IO.File]::SetLastWriteTimeUtc($raw, $rawStamp)
$leafSource = Join-Path $root 'leaf/wwwroot/leaf.txt'
$leafStamp = [IO.File]::GetLastWriteTimeUtc($leafSource)
$oldLeafAlias = @(Get-ChildItem (Join-Path $dist 'vendor/leaf') -Filter 'leaf.*.txt')
if ($oldLeafAlias.Count -ne 1) { throw 'Missing initial transitive fingerprint.' }
$leafText = 'leaf edits'
[IO.File]::WriteAllText($leafSource, [IO.File]::ReadAllText($leafSource).Replace('leaf asset', $leafText))
[IO.File]::SetLastWriteTimeUtc($leafSource, $leafStamp)
$newCss = Publish-Rcl 'rcl-project-edited' 'cyan'
if ($oldCss -eq $newCss -or (Test-Path -LiteralPath $oldCss) -or (Test-Path (Join-Path $dist '_content/Different.Library.Package/library.txt'))) {
    throw 'Retired RCL assets remain in the published site.'
}
if ([IO.File]::ReadAllText((Join-Path $dist 'raw.txt')) -ne 'static after!') { throw 'Same-timestamp static edit was lost.' }
$newLeafAlias = @(Get-ChildItem (Join-Path $dist 'vendor/leaf') -Filter 'leaf.*.txt')
if ($newLeafAlias.Count -ne 1 -or (Test-Path -LiteralPath $oldLeafAlias[0].FullName) -or
    [IO.File]::ReadAllText($newLeafAlias[0].FullName).Trim() -ne $leafText) {
    throw 'Same-size/same-mtime transitive edit kept a stale fingerprint or bytes.'
}
if ($htmlStamp -ne [IO.File]::GetLastWriteTimeUtc((Join-Path $dist 'index.html'))) { throw 'CSS-only changes rewrote unchanged HTML.' }

# Consume the same graph as NuGet libraries, exercising package asset paths too.
foreach ($library in @('leaf', 'library')) {
    Invoke-RclBuild "rcl-pack-$library" @('pack', (Join-Path $root $library), '-c', 'Release', '-o', (Join-Path $RunRoot 'feed'))
}
[IO.File]::WriteAllText($project, [IO.File]::ReadAllText($project).Replace('<ProjectReference Include="../library/Library.csproj" />', '<PackageReference Include="Different.Library.Package" Version="1.0.0" />'))
$null = Publish-Rcl 'rcl-nuget' 'cyan'

$razorProject = [IO.File]::ReadAllText($project)
try {
    [IO.File]::WriteAllText($project, $razorProject.Replace('Microsoft.NET.Sdk.Razor', 'Microsoft.NET.Sdk.Web'))
    $null = Publish-Rcl 'rcl-web-sdk' 'cyan'
    foreach ($invalid in @('plain-sdk', 'disabled-assets')) {
        $xml = if ($invalid -eq 'plain-sdk') { $razorProject.Replace('Microsoft.NET.Sdk.Razor', 'Microsoft.NET.Sdk') }
            else { $razorProject.Replace('<OutputType>', '<StaticWebAssetsEnabled>false</StaticWebAssetsEnabled><OutputType>') }
        [IO.File]::WriteAllText($project, $xml)
        $log = Join-Path $RunRoot "rcl-$invalid.log"
        & dotnet build $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" *> $log
        if (!$LASTEXITCODE -or !([IO.File]::ReadAllText($log)).Contains('KIJI1003')) {
            throw "Missing SDK configuration diagnostic: $invalid"
        }
    }
} finally { [IO.File]::WriteAllText($project, $razorProject) }
Write-Output 'RCL project/package assets, Razor/Web SDKs, configuration diagnostics, cleanup, and HTML reuse passed.'
