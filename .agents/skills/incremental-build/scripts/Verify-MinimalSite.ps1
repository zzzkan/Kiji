param([Parameter(Mandatory)][string] $RunRoot, [Parameter(Mandatory)][string] $PackageVersion)
$ErrorActionPreference = 'Stop'
$site = Join-Path $RunRoot 'minimal-site'
New-Item -ItemType Directory -Force $site | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="Kiji" Version="$PackageVersion" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $site 'Minimal.csproj')
@'
using Kiji;
var site = StaticSite.Create(args);
site.Info = new() { Name = "Minimal", BaseUrl = new Uri("https://example.test/") };
site.AddStaticPages();
return await site.RunAsync();
'@ | Set-Content (Join-Path $site 'Program.cs')
'@page "/"' + "`n<h1>Minimal site</h1>" | Set-Content (Join-Path $site 'Home.razor')

$project = Join-Path $site 'Minimal.csproj'
$original = [IO.File]::ReadAllText($project)
try {
foreach ($mode in @('publish', 'no-build', 'compressed', 'project-compressed', 'global-uncompressed', 'default-restored')) {
    $projectText = $original
    if ($mode -in @('project-compressed', 'global-uncompressed')) {
        $projectText = $projectText.Replace('</PropertyGroup>', '<CompressionEnabled>true</CompressionEnabled></PropertyGroup>')
    }
    if ([IO.File]::ReadAllText($project) -cne $projectText) { [IO.File]::WriteAllText($project, $projectText) }
    [string[]] $extra = switch ($mode) {
        'no-build' { '--no-build' }
        'compressed' { '-p:CompressionEnabled=true' }
        'global-uncompressed' { '-p:CompressionEnabled=false' }
        default { @() }
    }
    $compressed = $mode -in @('compressed', 'project-compressed')
    & dotnet publish $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" @extra *> (Join-Path $RunRoot "minimal-$mode.log")
    if ($LASTEXITCODE) { throw "Minimal site failed: $mode. See $RunRoot." }
    $files = @(Get-ChildItem (Join-Path $site 'dist') -Recurse -File)
    $expected = if ($compressed) { @('index.html', 'index.html.br', 'index.html.gz') } else { @('index.html') }
    if (Compare-Object $expected @($files.Name | Sort-Object)) {
        throw "Minimal site did not publish the expected compression setting: $mode."
    }
    if (!$compressed -and @($files | Where-Object Extension -In @('.gz', '.br')).Count) { throw "Unexpected compressed output: $mode." }
    $html = [IO.File]::ReadAllText((Join-Path $site 'dist/index.html'))
    if (!$html.Contains('<h1>Minimal site</h1>')) { throw "Missing minimal HTML: $mode." }
    if (!$compressed) { continue }
    foreach ($extension in @('br', 'gz')) {
        $source = [IO.File]::OpenRead((Join-Path $site "dist/index.html.$extension"))
        try {
            $codec = if ($extension -eq 'br') { [IO.Compression.BrotliStream]::new($source, [IO.Compression.CompressionMode]::Decompress) }
                else { [IO.Compression.GZipStream]::new($source, [IO.Compression.CompressionMode]::Decompress) }
            $reader = [IO.StreamReader]::new($codec)
            try { if ($reader.ReadToEnd() -ne $html) { throw "Compressed minimal HTML differs: $mode/$extension." } }
            finally { $reader.Dispose() }
        } finally { $source.Dispose() }
    }
}
} finally { [IO.File]::WriteAllText($project, $original) }

# Keep SDK variants in separate projects: the Web SDK supplies framework assets
# and hosting configuration in addition to Kiji's generated HTML.
$webSite = Join-Path $RunRoot 'minimal-web-options'
New-Item -ItemType Directory -Force $webSite | Out-Null
Copy-Item -LiteralPath (Join-Path $site 'Program.cs'), (Join-Path $site 'Home.razor') -Destination $webSite
foreach ($mode in @('default', 'project-compressed', 'global-uncompressed')) {
    $webProject = $original.Replace('Microsoft.NET.Sdk.Razor', 'Microsoft.NET.Sdk.Web')
    if ($mode -ne 'default') { $webProject = $webProject.Replace('</PropertyGroup>', '<CompressionEnabled>true</CompressionEnabled></PropertyGroup>') }
    [IO.File]::WriteAllText((Join-Path $webSite 'MinimalWeb.csproj'), $webProject)
    [string[]] $extra = if ($mode -eq 'global-uncompressed') { @('-p:CompressionEnabled=false') } else { @() }
    & dotnet publish $webSite -c Release "-p:RestorePackagesPath=$RunRoot/packages" @extra *> (Join-Path $RunRoot "minimal-web-$mode.log")
    if ($LASTEXITCODE) { throw "Minimal Web site failed: $mode." }
    $webHtml = Join-Path $webSite 'dist/index.html'
    if (![IO.File]::ReadAllText($webHtml).Contains('<h1>Minimal site</h1>')) { throw "Missing Web HTML: $mode." }
    foreach ($extension in @('br', 'gz')) {
        if ((Test-Path -LiteralPath "$webHtml.$extension") -ne ($mode -eq 'project-compressed')) { throw "Unexpected Web HTML compression: $mode/$extension." }
    }
}

# Exercise an external linked source, then remove the site's last isolated stylesheet.
# Neither physical source roots nor an old SDK manifest may prevent an empty site.
try {
    [IO.File]::WriteAllText((Join-Path $RunRoot 'linked.txt'), 'linked bytes')
    [IO.File]::WriteAllText($project, $original.Replace('</Project>', '<ItemGroup><Content Include="../linked.txt" Link="wwwroot/linked.txt" CopyToPublishDirectory="PreserveNewest" /></ItemGroup></Project>'))
    & dotnet publish $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" *> (Join-Path $RunRoot 'minimal-linked.log')
    if ($LASTEXITCODE -or [IO.File]::ReadAllText((Join-Path $site 'dist/linked.txt')) -ne 'linked bytes') { throw 'Linked source failed.' }
    [IO.File]::WriteAllText($project, $original)
    [IO.File]::WriteAllText((Join-Path $site 'Home.razor.css'), 'h1 { color: red; }')
    & dotnet publish $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" *> (Join-Path $RunRoot 'minimal-css.log')
    if ($LASTEXITCODE -or !(Test-Path (Join-Path $site 'dist/Minimal.styles.css'))) { throw 'Scoped CSS failed.' }
    [IO.File]::Delete((Join-Path $site 'Home.razor.css'))
    & dotnet publish $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" *> (Join-Path $RunRoot 'minimal-emptied.log')
    if ($LASTEXITCODE -or (Compare-Object @('index.html') @(Get-ChildItem (Join-Path $site 'dist') -File -Recurse | Select-Object -ExpandProperty Name | Sort-Object))) { throw 'Retired assets remain.' }
    foreach ($kind in @('runtime', 'publish.runtime')) {
        $manifest = Get-Content (Join-Path $site "bin/Release/net10.0/Minimal.staticwebassets.$kind.json") -Raw | ConvertFrom-Json
        if ($manifest.ContentRoots.Count) { throw "Stale asset roots remain: $kind" }
    }
} finally { [IO.File]::WriteAllText($project, $original); [IO.File]::Delete((Join-Path $site 'Home.razor.css')) }
$oldUrls = $env:ASPNETCORE_URLS
$oldBrowser = $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER
$oldPackages = $env:NUGET_PACKAGES
$watchProcess = $null
try {
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:0'
    $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = '1'
    $env:NUGET_PACKAGES = Join-Path $RunRoot 'packages'
    $watchLog = Join-Path $RunRoot 'minimal-watch.log'
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = @('watch', '--project', "`"$site`"", '--non-interactive')
        PassThru = $true
        RedirectStandardOutput = $watchLog
        RedirectStandardError = (Join-Path $RunRoot 'minimal-watch-error.log')
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $watchProcess = Start-Process @start
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        Start-Sleep -Milliseconds 1000
        if ($watchProcess.HasExited) { throw 'Minimal site watch exited.' }
        $log = Get-Content $watchLog -Raw
        if ($log -match 'http://127\.0\.0\.1:(\d+)') {
            try {
                $response = Invoke-WebRequest ($Matches[0] + '/') -TimeoutSec 2
                if ($response.StatusCode -eq 200 -and $response.Content.Contains('<h1>Minimal site</h1>')) { $ready = $true; break }
            } catch { }
        }
    }
    if (!$ready) { throw 'Minimal site watch did not serve HTML.' }
} finally {
    if ($null -ne $watchProcess -and !$watchProcess.HasExited) {
        if ($IsWindows) {
            & taskkill /PID $watchProcess.Id /T /F *> (Join-Path $RunRoot 'minimal-watch-stop.log')
            if ($LASTEXITCODE) { throw 'Failed to stop minimal site watch.' }
        } else { $watchProcess.Kill($true) }
        $watchProcess.WaitForExit()
    }
    $env:ASPNETCORE_URLS = $oldUrls
    $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = $oldBrowser
    $env:NUGET_PACKAGES = $oldPackages
}
Write-Output 'Minimal site publish, no-build publish, and watch passed without static assets.'
