param()
$Repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$ErrorActionPreference = 'Stop'
$work = Join-Path $Repository ('artifacts/static-assets-check/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null

$checks = [Collections.Generic.List[string]]::new()
function Write-File([string]$path, [string]$text) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $text)
}
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $checks.Add($message)
}
function Run-Dotnet([string[]]$arguments, [string]$log) {
    & dotnet @arguments *> $log
    if ($LASTEXITCODE) { Get-Content $log -Tail 25; throw "dotnet failed: $arguments" }
}
function Bytes-Equal([string]$left, [string]$right) {
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes($left)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($right))
}
function Check-Compressed([string]$path, [string]$format) {
    $suffix = if ($format -eq 'gzip') { '.gz' } else { '.br' }
    Check (Test-Path -LiteralPath ($path + $suffix)) "Compressed output exists: $path$suffix"
    $inputStream = [IO.File]::OpenRead($path + $suffix)
    $decoder = if ($format -eq 'gzip') {
        [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    } else {
        [IO.Compression.BrotliStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
    }
    $outputStream = [IO.MemoryStream]::new()
    try {
        $decoder.CopyTo($outputStream)
        Check ([Convert]::ToBase64String($outputStream.ToArray()) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) "Compression preserves bytes: $path$suffix"
    } finally { $decoder.Dispose(); $inputStream.Dispose(); $outputStream.Dispose() }
}

Write-File (Join-Path $work 'Directory.Build.props') '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>'
Write-File (Join-Path $work 'Directory.Build.targets') '<Project />'
Write-File (Join-Path $work 'Directory.Packages.props') '<Project />'
$feed = Join-Path $work 'feed'
Run-Dotnet @('pack', (Join-Path $Repository 'src/Kiji'), '-c', 'Release', '-o', $feed) (Join-Path $work 'pack.log')
$package = Get-ChildItem $feed -Filter '*.nupkg' | Select-Object -First 1
$version = $package.BaseName.Substring('Kiji.'.Length)
Write-File (Join-Path $work 'NuGet.Config') "<configuration><packageSources><clear/><add key=`"local`" value=`"$feed`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>"
$packages = Join-Path $work 'packages'

# Two libraries exercise transitive discovery, custom public prefixes and CSS isolation.
Write-File (Join-Path $work 'leaf/Leaf.csproj') '<Project Sdk="Microsoft.NET.Sdk.Razor"><PropertyGroup><PackageId>Assets.Leaf</PackageId><StaticWebAssetBasePath>vendor/leaf</StaticWebAssetBasePath></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup><Target Name="RecordCompileVisit" BeforeTargets="CoreCompile"><WriteLinesToFile File="$(IntermediateOutputPath)kiji-compile-visits.txt" Lines="$(StaticWebAssetsCacheDefineStaticWebAssetsEnabled)" Overwrite="false" /></Target></Project>'
$leafBuildLog = Join-Path $work 'leaf/obj/Release/net10.0/kiji-compile-visits.txt'
Write-File (Join-Path $work 'leaf/wwwroot/leaf.js') ('export const leaf = "' + ('abcde' * 500) + '";')
Write-File (Join-Path $work 'library/Library.csproj') '<Project Sdk="Microsoft.NET.Sdk.Razor"><PropertyGroup><PackageId>Assets.Library</PackageId></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><ProjectReference Include="../leaf/Leaf.csproj" /></ItemGroup></Project>'
Write-File (Join-Path $work 'library/Card.razor') '<p>Library component</p>'
Write-File (Join-Path $work 'library/Card.razor.css') 'p { color: blue; }'
Write-File (Join-Path $work 'library/wwwroot/library.svg') '<svg xmlns="http://www.w3.org/2000/svg"><circle r="10"/></svg>'
Write-File (Join-Path $work 'library/wwwroot/Assets.Library.lib.module.js') 'export function afterStarted() { /* library initializer */ }'
Write-File (Join-Path $work 'linked.css') ('/* linked */ body { color: blue; }' * 100)

$assetContents = [ordered]@{
    'app.css' = 'body { color: red; }' * 200
    'app.js' = 'export const app = "' + ('abcde' * 1000) + '";'
    'module.mjs' = 'export const module = "' + ('abcde' * 1000) + '";'
    'Audit.lib.module.js' = 'export function afterStarted() { /* site initializer */ }'
    'font.woff2' = 'wOF2 opaque font payload'
    'image.png' = 'opaque image payload'
    'image.svg' = '<svg xmlns="http://www.w3.org/2000/svg"><circle r="10"/></svg>'
    'data.json' = '{"value":"' + ('abcde' * 1000) + '"}'
    'data.xml' = '<value>' + ('abcde' * 1000) + '</value>'
    'download.pdf' = '%PDF-1.4 opaque download payload'
    'module.wasm' = 'opaque wasm payload'
    'static.html' = '<p>Existing static HTML</p>' * 200
    '日本語/space #%.svg' = '<svg xmlns="http://www.w3.org/2000/svg"><rect width="10"/></svg>'
    'nested/square[1].svg' = '<svg xmlns="http://www.w3.org/2000/svg"><rect width="20"/></svg>'
}
$assetKeys = @($assetContents.Keys) + @('linked.css', 'Audit.styles.css', '_content/Assets.Library/library.svg', '_content/Assets.Library/Assets.Library.lib.module.js', 'vendor/leaf/leaf.js')
$pathsCode = ($assetKeys | ForEach-Object { '"' + $_ + '"' }) -join ','
foreach ($kind in @('razor', 'web', 'empty')) {
    $site = Join-Path $work $kind
    $sdk = if ($kind -eq 'web') { 'Microsoft.NET.Sdk.Web' } else { 'Microsoft.NET.Sdk.Razor' }
    $items = if ($kind -ne 'empty') { @'
<PropertyGroup><ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest></PropertyGroup>
<ItemGroup>
  <ProjectReference Include="../library/Library.csproj" />
  <StaticWebAssetFingerprintPattern Include="Module" Pattern="*.mjs" Expression="#[.{fingerprint}]!" />
  <Content Include="../linked.css" Link="wwwroot/linked.css" />
  <Content Update="wwwroot/never.txt" CopyToPublishDirectory="Never" />
  <ServiceWorker Include="wwwroot/service-worker.js" PublishedContent="service-worker.published.js" />
</ItemGroup>
'@ } else { '' }
    Write-File (Join-Path $site 'Audit.csproj') "<Project Sdk=`"$sdk`"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><PackageReference Include=`"Kiji`" Version=`"$version`" /></ItemGroup>$items</Project>"
    Write-File (Join-Path $site 'Program.cs') @'
using Kiji;
using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
var site = StaticSite.Create(args);
site.Info = new() { Name = "Asset audit", BaseUrl = new Uri("https://example.test/kiji/") };
site.AddStaticPages();
site.UseMarkdownContent<Dictionary<string, object>>();
site.AddArtifact("meta/generated.json", async (output, _, token) =>
    await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"generated\":true}"), token));
site.AddRssFeed(_ => [new FeedItem("Post", new string('r', 3000), DateTimeOffset.UnixEpoch, "post/")]);
site.AddSitemap();
await site.RunAsync();
'@
    if ($kind -eq 'empty') {
        Write-File (Join-Path $site 'Home.razor') '@page "/"
<h1>Empty site</h1>'
    } else {
        Write-File (Join-Path $site 'contents/bundle/index.md') "---`ntitle: Bundle`n---`n![Generated image](cover.png)"
        Copy-Item -LiteralPath (Join-Path $Repository 'docs/contents/getting-started/smallest-site.png') -Destination (Join-Path $site 'contents/bundle/cover.png')
        Write-File (Join-Path $site 'Bundle.razor') @'
@page "/bundle/"
@using Kiji
@using Kiji.Markdown
@inject ContentDictionary<MarkdownContent<Dictionary<string, object>>> Posts
@((Microsoft.AspNetCore.Components.MarkupString)body)
@code {
    private string body = "";
    protected override async Task OnInitializedAsync() => body = await Posts.Single().Value.RenderAsync();
}
'@
        foreach ($entry in $assetContents.GetEnumerator()) { Write-File (Join-Path $site ('wwwroot/' + $entry.Key)) $entry.Value }
        Write-File (Join-Path $site 'wwwroot/never.txt') 'never published'
        Write-File (Join-Path $site 'wwwroot/service-worker.js') '// development-worker'
        Write-File (Join-Path $site 'service-worker.published.js') '// published-worker'
        Write-File (Join-Path $site 'Home.razor.css') 'h1 { color: green; }'
        Write-File (Join-Path $site 'Home.razor') (@'
@page "/"
@using Microsoft.AspNetCore.Components
<h1>Asset integration</h1>
@foreach (var path in new string[] { PATHS })
{
    <a data-asset="@path" href="@Assets[path]">asset</a>
}
<ImportMap />
<Library.Card />
<p>@(new string('x', 10000))</p>
'@).Replace('PATHS', $pathsCode)
    }
    $publishArgs = @('publish', (Join-Path $site 'Audit.csproj'), '-c', 'Release', '-o', (Join-Path $site 'dist'), "-p:RestorePackagesPath=$packages", '-p:CompressionEnabled=true')
    $leafVisitsBefore = if (Test-Path -LiteralPath $leafBuildLog) { [IO.File]::ReadAllLines($leafBuildLog).Length } else { 0 }
    Run-Dotnet $publishArgs (Join-Path $work "$kind-publish.log")
    if ($kind -ne 'empty') {
        $leafVisits = [IO.File]::ReadAllLines($leafBuildLog)
        Check ($leafVisits.Length -eq $leafVisitsBefore + 1) "$kind builds the transitive library once without conflicting MSBuild instances"
        Check ($leafVisits[-1] -eq 'false') "$kind applies content-based asset validation to the transitive library"
    }
    $dist = Join-Path $site 'dist'
    $html = Get-Content (Join-Path $dist 'index.html') -Raw
    Check ($html.Contains('<h1')) "$kind generated HTML"
    Check (!(Test-Path -LiteralPath (Join-Path $dist 'Audit.dll'))) "$kind publishes only the static site"
    if ($kind -eq 'empty') {
        Run-Dotnet @($publishArgs | Where-Object { $_ -ne '-p:CompressionEnabled=true' }) (Join-Path $work 'empty-default-compression.log')
        Check ([IO.File]::ReadAllText((Join-Path $dist 'index.html')) -ceq $html) 'Empty site publishes identical HTML without compression'
        Check (@(Get-ChildItem $dist -Recurse -File | Where-Object Extension -In @('.gz','.br')).Count -eq 0) 'Empty site removes obsolete compression outputs'
        continue
    }
    $links = @{}
    foreach ($match in [regex]::Matches($html, '<a data-asset="([^"]+)" href="([^"]+)"')) {
        $links[[Net.WebUtility]::HtmlDecode($match.Groups[1].Value)] = [Net.WebUtility]::HtmlDecode($match.Groups[2].Value)
    }
    foreach ($key in $assetKeys) {
        Check ($links.ContainsKey($key)) "$kind renders asset link: $key"
        $relative = [Uri]::UnescapeDataString($links[$key].Substring('/kiji/'.Length))
        Check ($relative -cne $key) "$kind resolves fingerprint URL: $key"
        Check (Test-Path -LiteralPath (Join-Path $dist $relative)) "$kind materializes fingerprint URL: $key"
        if ($assetContents.Contains($key)) {
            Check (Bytes-Equal (Join-Path $site ('wwwroot/' + $key)) (Join-Path $dist $relative)) "$kind preserves source bytes: $key"
        }
        if ($key -in @('app.css','app.js','module.mjs','Audit.lib.module.js','_content/Assets.Library/Assets.Library.lib.module.js','data.json','data.xml','static.html','linked.css','vendor/leaf/leaf.js')) {
            Check-Compressed (Join-Path $dist $relative) 'gzip'
            Check-Compressed (Join-Path $dist $relative) 'brotli'
        }
    }
    Check ($html.Contains('type="importmap"')) "$kind renders ImportMap"
    Check ($html.Contains('"integrity"')) "$kind publishes integrity metadata"
    Check (!(Test-Path -LiteralPath (Join-Path $dist 'never.txt'))) "$kind excludes CopyToPublishDirectory=Never"
    Check ((Get-Content (Join-Path $dist 'service-worker.js') -Raw).Contains('published-worker')) "$kind selects the publish service worker"
    Check-Compressed (Join-Path $dist 'service-worker.js') 'gzip'
    Check-Compressed (Join-Path $dist 'service-worker.js') 'brotli'
    $workerManifest = Get-Content (Join-Path $dist 'service-worker-assets.js') -Raw
    Check ($workerManifest.Contains('index.html')) "$kind includes generated HTML in service worker manifest"
    $workerEntries = ($workerManifest.Substring($workerManifest.IndexOf('{')).Trim().TrimEnd(';') | ConvertFrom-Json).assets
    Check (@($workerEntries | Where-Object url -Match '(^|/)service-worker(\.|-assets)').Count -eq 0) "$kind excludes replaced worker sources from precache"
    foreach ($entry in $workerEntries) {
        Check (!$entry.url.StartsWith('/') -and !$entry.url.StartsWith('_content/Audit/')) "$kind worker URLs are relative to the deployed site"
        $entryFile = Join-Path $dist ([Uri]::UnescapeDataString($entry.url))
        Check ([IO.File]::Exists($entryFile)) "$kind worker cache URL exists: $($entry.url)"
        $hash = 'sha256-' + [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($entryFile)))
        Check ($hash -ceq $entry.hash) "$kind worker cache integrity matches published bytes: $($entry.url)"
    }
    Check (@(Get-ChildItem $dist -Filter 'service-worker.*.js').Count -eq 0) "$kind excludes development-worker fingerprint aliases"
    Check-Compressed (Join-Path $dist 'index.html') 'gzip'
    Check-Compressed (Join-Path $dist 'index.html') 'brotli'
    $fullManifest = Get-Content (Join-Path $site 'obj/Release/net10.0/staticwebassets.publish.json') -Raw | ConvertFrom-Json
    $initializers = @($fullManifest.Assets | Where-Object { $_.AssetTraitName -eq 'JSModule' -and $_.AssetTraitValue -eq 'JSLibraryModule' })
    Check ($initializers.Count -eq 2) "$kind resolves site and RCL initializer metadata"
    $modulePaths = Get-Content (Join-Path $dist 'Audit.modules.json') -Raw | ConvertFrom-Json
    Check (@($modulePaths).Count -eq 2) "$kind publishes initializer manifest"
    foreach ($module in $modulePaths) { Check (Test-Path -LiteralPath (Join-Path $dist $module)) "$kind initializer manifest points to a public module: $module" }
    $bundleHtml = [IO.File]::ReadAllText((Join-Path $dist 'bundle/index.html'))
    $imageUrl = [regex]::Match($bundleHtml, '<img[^>]* src="([^"]+)"').Groups[1].Value
    Check ($imageUrl.StartsWith('/kiji/bundle/') -and $imageUrl.EndsWith('.webp')) "$kind renders a stable page-bundle image URL"
    $imageRelativePath = $imageUrl.Substring('/kiji/'.Length)
    Check (Test-Path -LiteralPath (Join-Path $dist $imageRelativePath)) "$kind publishes processed page-bundle image"
    Check (@($fullManifest.Assets | Where-Object RelativePath -EQ $imageRelativePath).Count -eq 1) "$kind registers processed image with SDK"
    Check (@($workerEntries | Where-Object url -EQ $imageRelativePath).Count -eq 1) "$kind includes processed image in worker manifest"
    Check-Compressed (Join-Path $dist 'meta/generated.json') 'gzip'
    Check-Compressed (Join-Path $dist 'meta/generated.json') 'brotli'
    Check (@($workerEntries | Where-Object url -EQ 'meta/generated.json').Count -eq 1) "$kind includes custom artifact in worker manifest"
    Check-Compressed (Join-Path $dist 'feed.xml') 'gzip'
    Check-Compressed (Join-Path $dist 'feed.xml') 'brotli'
    Check (Test-Path -LiteralPath (Join-Path $dist 'sitemap.xml')) "$kind preserves sitemap URL"
    Check (@($fullManifest.Assets | Where-Object { $_.RelativePath -eq 'index.html' -and $_.Fingerprint }).Count -eq 1) "$kind SDK fingerprints generated HTML without changing its URL"
    # Incremental CSS edit must update HTML, physical aliases, compression and retire old URLs.
    $oldWorker = Get-Content (Join-Path $dist 'service-worker.js') -Raw
    $oldCss = [Uri]::UnescapeDataString($links['app.css'].Substring('/kiji/'.Length))
    Write-File (Join-Path $site 'wwwroot/app.css') ('body { color: cyan; }' * 200)
    Run-Dotnet $publishArgs (Join-Path $work "$kind-republish.log")
    $newHtml = Get-Content (Join-Path $dist 'index.html') -Raw
    Check (!$newHtml.Contains($links['app.css'])) "$kind invalidates HTML after CSS changes"
    Check (!(Test-Path -LiteralPath (Join-Path $dist $oldCss))) "$kind retires old fingerprint URL"
    Check-Compressed (Join-Path $dist 'index.html') 'brotli'
    Check ((Get-Content (Join-Path $dist 'service-worker.js') -Raw) -cne $oldWorker) "$kind updates the worker version after a page asset change"
    Check-Compressed (Join-Path $dist 'service-worker.js') 'gzip'
    Check-Compressed (Join-Path $dist 'service-worker.js') 'brotli'
    $sourceJs = Join-Path $site 'wwwroot/app.js'
    $stamp = [IO.File]::GetLastWriteTimeUtc($sourceJs)
    $beforeJs = [IO.File]::ReadAllText($sourceJs)
    [IO.File]::WriteAllText($sourceJs, $beforeJs.Replace('abcde', 'vwxyz'))
    [IO.File]::SetLastWriteTimeUtc($sourceJs, $stamp)
    Run-Dotnet $publishArgs (Join-Path $work "$kind-same-stamp.log")
    $changedHtml = Get-Content (Join-Path $dist 'index.html') -Raw
    Check (!$changedHtml.Contains($links['app.js'])) "$kind invalidates the SDK hash after equal-size/equal-mtime asset edits"
    Check (Bytes-Equal $sourceJs (Join-Path $dist 'app.js')) "$kind updates the stable URL after equal-size/equal-mtime asset edits"
    $jsUrl = [regex]::Match($changedHtml, '<a data-asset="app.js" href="([^"]+)"').Groups[1].Value
    $jsPath = Join-Path $dist ($jsUrl.Substring('/kiji/'.Length))
    Check (Bytes-Equal $sourceJs $jsPath) "$kind publishes current bytes at the new fingerprint URL"
    Check-Compressed $jsPath 'gzip'
    Check-Compressed $jsPath 'brotli'
    # An editor may change an input while the SDK is compressing it after rendering.
    # The final publish must fail before replacing any previously published files.
    $project = Join-Path $site 'Audit.csproj'
    $projectXml = [IO.File]::ReadAllText($project)
    $savedJs = [IO.File]::ReadAllText($sourceJs)
    function Published-Hashes {
        @(Get-ChildItem $dist -File -Recurse | Sort-Object FullName | ForEach-Object {
            [IO.Path]::GetRelativePath($dist, $_.FullName) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
        })
    }
    $beforePublish = Published-Hashes
    try {
        $mutation = '<Target Name="ChangeAssetAfterRendering" AfterTargets="KijiGenerateSite"><WriteLinesToFile File="$(MSBuildProjectDirectory)/wwwroot/app.js" Lines="changed-after-render" Overwrite="true" /></Target>'
        [IO.File]::WriteAllText($project, $projectXml.Replace('</Project>', $mutation + '</Project>'))
        $failureLog = Join-Path $work "$kind-late-source-change.log"
        & dotnet @publishArgs *> $failureLog
        Check ($LASTEXITCODE -ne 0 -and [IO.File]::ReadAllText($failureLog).Contains('KIJI1005')) "$kind rejects source changes during SDK optimization"
        Check (!(Compare-Object $beforePublish (Published-Hashes))) "$kind keeps the last published files on late input failure"
    } finally {
        [IO.File]::WriteAllText($project, $projectXml)
        [IO.File]::WriteAllText($sourceJs, $savedJs)
    }
    Run-Dotnet $publishArgs (Join-Path $work "$kind-after-late-source-change.log")
    # Compression is optional; worker manifests still need generated pages.
    $noCompressionArgs = @($publishArgs | Where-Object { $_ -ne '-p:CompressionEnabled=true' }) + @('-p:CompressionEnabled=false')
    Run-Dotnet $noCompressionArgs (Join-Path $work "$kind-no-compression-worker.log")
    Check (@(Get-ChildItem $dist -Recurse -File | Where-Object Extension -In @('.gz','.br')).Count -eq 0) "$kind removes obsolete sidecars across RCL references"
    $workerText = [IO.File]::ReadAllText((Join-Path $dist 'service-worker-assets.js'))
    $workerJson = $workerText.Substring($workerText.IndexOf('{')).TrimEnd(";`r`n ".ToCharArray()) | ConvertFrom-Json
    Check (@($workerJson.assets | Where-Object url -EQ 'index.html').Count -eq 1) "$kind retains generated pages in uncompressed worker manifest"
    foreach ($asset in $workerJson.assets) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $dist ([Uri]::UnescapeDataString($asset.url))))
        Check (('sha256-' + [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData($bytes))) -ceq $asset.hash) "$kind uncompressed worker integrity matches: $($asset.url)"
    }
    $workerlessProject = [regex]::Replace($projectXml, '<ServiceWorkerAssetsManifest>.*?</ServiceWorkerAssetsManifest>|<ServiceWorker Include="[^"]+" PublishedContent="[^"]+" />', '')
    try {
        [IO.File]::WriteAllText($project, $workerlessProject)
        Run-Dotnet $noCompressionArgs (Join-Path $work "$kind-no-compression-direct.log")
        $manifest = Get-Content (Join-Path $site 'obj/Release/net10.0/staticwebassets.publish.json') -Raw | ConvertFrom-Json
        Check (@($manifest.Assets | Where-Object RelativePath -EQ 'index.html').Count -eq 0) "$kind skips unused generated asset registration"
        foreach ($path in @('index.html','feed.xml','sitemap.xml','meta/generated.json',$imageRelativePath)) { Check (Test-Path -LiteralPath (Join-Path $dist $path)) "$kind directly publishes $path" }
        Check (!(Test-Path -LiteralPath (Join-Path $dist 'service-worker-assets.js'))) "$kind retires removed worker manifest"
        $directHtml = [IO.File]::ReadAllText((Join-Path $dist 'index.html'))
        $directStamp = [IO.File]::GetLastWriteTimeUtc($sourceJs)
        [IO.File]::WriteAllText($sourceJs, $savedJs.Replace('vwxyz', 'VWXYZ'))
        [IO.File]::SetLastWriteTimeUtc($sourceJs, $directStamp)
        Run-Dotnet $noCompressionArgs (Join-Path $work "$kind-direct-equal-stamp.log")
        $editedDirectHtml = [IO.File]::ReadAllText((Join-Path $dist 'index.html'))
        $oldDirectUrl = [regex]::Match($directHtml, '<a data-asset="app.js" href="([^"]+)"').Groups[1].Value
        $newDirectUrl = [regex]::Match($editedDirectHtml, '<a data-asset="app.js" href="([^"]+)"').Groups[1].Value
        Check ($oldDirectUrl -cne $newDirectUrl) "$kind updates fingerprint URL after equal-stamp input edit without compression"
        Check (Bytes-Equal $sourceJs (Join-Path $dist 'app.js')) "$kind updates stable input bytes during direct publication"
        Check (Bytes-Equal $sourceJs (Join-Path $dist $newDirectUrl.Substring('/kiji/'.Length))) "$kind updates fingerprint alias during direct publication"
        $directHtml = $editedDirectHtml
        Run-Dotnet ($noCompressionArgs + @('-p:KijiIncludeGeneratedStaticWebAssets=true')) (Join-Path $work "$kind-no-compression-forced-registration.log")
        $manifest = Get-Content (Join-Path $site 'obj/Release/net10.0/staticwebassets.publish.json') -Raw | ConvertFrom-Json
        Check (@($manifest.Assets | Where-Object RelativePath -EQ 'index.html').Count -eq 1) "$kind registers generated assets for custom SDK extensions"
        Check ([IO.File]::ReadAllText((Join-Path $dist 'index.html')) -ceq $directHtml) "$kind produces identical HTML with direct publication"
    } finally {
        [IO.File]::WriteAllText($project, $projectXml)
        [IO.File]::WriteAllText($sourceJs, $savedJs)
    }
    Run-Dotnet $publishArgs (Join-Path $work "$kind-restored-compression.log")
    Check-Compressed (Join-Path $dist 'index.html') 'gzip'
    Check-Compressed (Join-Path $dist 'service-worker.js') 'brotli'
}
$checks | ConvertTo-Json | Set-Content (Join-Path $work 'checks.json')
Write-Output "$($checks.Count) checks passed. Evidence: $work"

$ErrorActionPreference = 'Stop'
$root = $work
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
    $checks.Add($message)
}
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AutomaticDecompression = [Net.DecompressionMethods]::None
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(5)
try {
    foreach ($kind in @('razor','web','empty')) {
        $process = $null
        try {
            $site = Join-Path $root $kind
            $log = Join-Path $root "$kind-http.log"
            $errorLog = Join-Path $root "$kind-http-error.log"
            $process = Start-Process -FilePath dotnet -ArgumentList @('exec', "`"$site/bin/Release/net10.0/Audit.dll`"", '--urls', 'http://127.0.0.1:0') -WorkingDirectory $site -WindowStyle Hidden -PassThru -Environment @{ ASPNETCORE_ENVIRONMENT='Production'; DOTNET_ENVIRONMENT='Production' } -RedirectStandardOutput $log -RedirectStandardError $errorLog
            $url = $null
            for ($i=0; $i -lt 60; $i++) {
                Start-Sleep -Milliseconds 250
                if ($process.HasExited) { Get-Content $errorLog; throw "$kind server exited" }
                if ((Get-Content $log -Raw) -match 'http://127\.0\.0\.1:\d+') { $url = $Matches[0]; break }
            }
            if (!$url) { throw "$kind server did not start" }
            $html = $client.GetStringAsync("$url/kiji/").GetAwaiter().GetResult()
            Check ($html.Contains('<h1')) "$kind renders through dev fallback"
            Check ($html.Contains('/kiji/_kiji/livereload.js')) "$kind preserves base path in reload URL"
            if ($kind -eq 'empty') { continue }
            Check (!$html.Contains('"integrity"')) "$kind omits stale integrity during live editing"
            $links = @{}
            foreach ($match in [regex]::Matches($html, '<a data-asset="([^"]+)" href="([^"]+)"')) {
                $links[[Net.WebUtility]::HtmlDecode($match.Groups[1].Value)] = [Net.WebUtility]::HtmlDecode($match.Groups[2].Value)
            }
            foreach ($key in $links.Keys) {
                $response = $client.GetAsync($url + $links[$key]).GetAwaiter().GetResult()
                try {
                    Check ([int]$response.StatusCode -eq 200) "$kind serves fingerprint URL: $key"
                    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    Check ($bytes.Length -gt 0) "$kind asset body is present: $key"
                    Check ($response.Headers.ETag -ne $null) "$kind provides SDK ETag: $key"
                    Check ($response.Content.Headers.ContentType -ne $null) "$kind provides SDK content type: $key"
                    if ($key -eq 'app.css') { $cssEtag = $response.Headers.ETag.ToString() }
                    if (Test-Path -LiteralPath (Join-Path $site ('wwwroot/' + $key))) {
                        Check ([Convert]::ToBase64String($bytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $site ('wwwroot/' + $key))))) "$kind serves original bytes: $key"
                    }
                } finally { $response.Dispose() }
            }
            foreach ($mode in @('gzip','range','conditional')) {
                $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $url + $links['app.css'])
                if ($mode -eq 'gzip') { $request.Headers.TryAddWithoutValidation('Accept-Encoding', 'gzip') | Out-Null }
                if ($mode -eq 'range') { $request.Headers.TryAddWithoutValidation('Range', 'bytes=0-9') | Out-Null }
                if ($mode -eq 'conditional') { $request.Headers.TryAddWithoutValidation('If-None-Match', $cssEtag) | Out-Null }
                $response = $client.SendAsync($request).GetAwaiter().GetResult()
                try {
                    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    if ($mode -eq 'conditional') { Check ([int]$response.StatusCode -eq 304) "$kind handles If-None-Match" }
                    if ($mode -eq 'range') {
                        [pscustomobject]@{ Kind=$kind; Status=[int]$response.StatusCode; Bytes=$bytes.Length; Headers=$response.ToString() } | ConvertTo-Json | Set-Content (Join-Path $root "$kind-range.json")
                        Check ([int]$response.StatusCode -eq 206 -and $bytes.Length -eq 10) "$kind handles byte ranges"
                    }
                    if ($mode -eq 'gzip') {
                        Check ($response.Content.Headers.ContentEncoding.Contains('gzip')) "$kind negotiates SDK gzip"
                        $inputStream = [IO.MemoryStream]::new($bytes)
                        $decoder = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
                        $decoded = [IO.MemoryStream]::new()
                        try {
                            $decoder.CopyTo($decoded)
                            Check ([Text.Encoding]::UTF8.GetString($decoded.ToArray()) -ceq [IO.File]::ReadAllText((Join-Path $site 'wwwroot/app.css'))) "$kind negotiated compression preserves bytes"
                        } finally { $decoded.Dispose(); $decoder.Dispose(); $inputStream.Dispose() }
                    }
                } finally { $response.Dispose(); $request.Dispose() }
            }
            $changed = 'body { color: teal; }' * 200
            [IO.File]::WriteAllText((Join-Path $site 'wwwroot/app.css'), $changed)
            Check ($client.GetStringAsync($url + $links['app.css']).GetAwaiter().GetResult() -ceq $changed) "$kind serves live edits at the existing fingerprint URL"
            [IO.File]::WriteAllText((Join-Path $site 'wwwroot/new.css'), 'body{}')
            Check ($client.GetStringAsync("$url/kiji/new.css").GetAwaiter().GetResult() -ceq 'body{}') "$kind discovers files added after build"
            [IO.File]::Delete((Join-Path $site 'wwwroot/app.css'))
            $response = $client.GetAsync($url + $links['app.css']).GetAwaiter().GetResult()
            try { Check ([int]$response.StatusCode -eq 404) "$kind returns 404 for deleted known assets" } finally { $response.Dispose() }
            $response = $client.GetAsync("$url/app.css").GetAwaiter().GetResult()
            try { Check ([int]$response.StatusCode -eq 404) "$kind rejects requests outside the base path" } finally { $response.Dispose() }
        } finally {
            if ($null -ne $process -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            "Task-owned $kind server stopped." | Add-Content (Join-Path $root 'server-stop.log')
        }
    }
} finally { $client.Dispose(); $handler.Dispose() }
$checks | ConvertTo-Json | Set-Content (Join-Path $root 'http-checks.json')
Write-Output "$($checks.Count) HTTP checks passed. Evidence: $root"
