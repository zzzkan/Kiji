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

foreach ($mode in @('publish', 'no-build')) {
    [string[]] $extra = if ($mode -eq 'no-build') { @('--no-build') } else { @() }
    & dotnet publish $site -c Release "-p:RestorePackagesPath=$RunRoot/packages" @extra *> (Join-Path $RunRoot "minimal-$mode.log")
    if ($LASTEXITCODE) { throw "Minimal site failed: $mode. See $RunRoot." }
    $files = @(Get-ChildItem (Join-Path $site 'dist') -Recurse -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'index.html' -or !([IO.File]::ReadAllText($files[0].FullName)).Contains('<h1>Minimal site</h1>')) {
        throw "Minimal site did not publish HTML only: $mode."
    }
}

# Exercise an external linked source, then remove the site's last isolated stylesheet.
# Neither physical source roots nor an old SDK manifest may prevent an empty site.
$project = Join-Path $site 'Minimal.csproj'
$original = [IO.File]::ReadAllText($project)
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
    if ($LASTEXITCODE -or @(Get-ChildItem (Join-Path $site 'dist') -File -Recurse).Count -ne 1) { throw 'Retired assets remain.' }
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
