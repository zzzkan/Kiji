param([string] $RunRoot, [string] $Site, [string] $PackageVersion)
$ErrorActionPreference = 'Stop'

# Read the DLL's own debug directory: an old PDB left on disk is not proof of symbols.
$inspector = Join-Path $RunRoot 'inspector'
New-Item -ItemType Directory -Path $inspector -Force | Out-Null
'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>' | Set-Content (Join-Path $inspector 'Inspector.csproj')
@'
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var debug = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {
    Mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid),
    Symbols = debug.Length != 0,
    PdbPath = debug.Length == 0 ? null : pe.ReadCodeViewDebugDirectoryData(debug[0]).Path
}));
'@ | Set-Content (Join-Path $inspector 'Program.cs')
& dotnet build $inspector -c Release > (Join-Path $RunRoot 'inspector.log') 2>&1
if ($LASTEXITCODE) { throw 'Inspector build failed.' }
$inspectorDll = Join-Path $inspector 'bin/Release/net10.0/Inspector.dll'

function Read-Identity([string] $dll) {
    $json = & dotnet $inspectorDll $dll
    if ($LASTEXITCODE) { throw "Cannot inspect $dll" }
    return $json | ConvertFrom-Json
}
function Invoke-Mode([string] $label, [string] $verb, [string] $configuration, [bool] $symbols, [string[]] $extra = @()) {
    & dotnet $verb $Site -c $configuration -p:SourceRevisionId=build-modes "-p:RestorePackagesPath=$RunRoot/packages" @extra > (Join-Path $RunRoot "$label.log") 2>&1
    if ($LASTEXITCODE) { throw "Build mode failed: $label" }
    $identity = Read-Identity (Join-Path $Site "bin/$configuration/net10.0/Portable.dll")
    if ($identity.Symbols -ne $symbols) { throw "Wrong symbol mode: $label" }
    $identity | ConvertTo-Json | Set-Content (Join-Path $RunRoot "$label-identity.json")
    return $identity
}

$release = Invoke-Mode 'mode-release-build' 'build' 'Release' $false
$published = Invoke-Mode 'mode-release-publish' 'publish' 'Release' $false
if ($release.Mvid -ne $published.Mvid) { throw 'Release build/publish changed MVID.' }
$noBuild = Invoke-Mode 'mode-release-no-build' 'publish' 'Release' $false @('--no-build')
if ($release.Mvid -ne $noBuild.Mvid) { throw 'Publish --no-build changed MVID.' }
# The switch controls publication, not whether this executable is a Release site.
$ordinary = Invoke-Mode 'mode-release-opt-out' 'build' 'Release' $false @('-p:KijiGenerateOnPublish=false')
if ($release.Mvid -ne $ordinary.Mvid) { throw 'Publish opt-out changed compilation identity.' }
$debug = Invoke-Mode 'mode-debug-build' 'build' 'Debug' $true
$debugPublish = Invoke-Mode 'mode-debug-publish' 'publish' 'Debug' $true
if ($debug.Mvid -ne $debugPublish.Mvid) { throw 'Debug build/publish changed MVID.' }
if ($debug.PdbPath.StartsWith('/_/source')) { throw 'Debug source paths were remapped.' }
$again = Invoke-Mode 'mode-release-again' 'build' 'Release' $false
if ($release.Mvid -ne $again.Mvid) { throw 'Returning from Debug changed Release identity.' }
# DebugType alone is not in the SDK's incremental compiler cache. Rebuild when
# testing an explicit compiler-option override; Kiji does not replace that cache.
$override = Invoke-Mode 'mode-release-explicit-symbols' 'build' 'Release' $true @('--no-incremental', '-p:DebugType=portable')
$restored = Invoke-Mode 'mode-release-restored' 'build' 'Release' $false @('--no-incremental')
if ($release.Mvid -ne $restored.Mvid) { throw 'Returning from explicit symbols changed Release identity.' }

# Import the packaged targets through a direct PackageReference in non-site projects too.
$nonSite = Join-Path $RunRoot 'non-site'
New-Item -ItemType Directory -Path $nonSite -Force | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup><PackageReference Include="Kiji" Version="$PackageVersion" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $nonSite 'NonSite.csproj')
'public static class Entry { public static void Main() { } }' | Set-Content (Join-Path $nonSite 'Entry.cs')
foreach ($mode in @('library', 'IsTestProject', 'TestProject', 'IsTestingPlatformApplication')) {
    [string[]] $extra = if ($mode -eq 'library') { @('-p:OutputType=Library') } else { @('-p:OutputType=Exe', "-p:${mode}=true") }
    & dotnet build $nonSite -c Release "-p:RestorePackagesPath=$RunRoot/packages" @extra > (Join-Path $RunRoot "mode-$mode.log") 2>&1
    if ($LASTEXITCODE) { throw "Non-site build failed: $mode" }
    $identity = Read-Identity (Join-Path $nonSite 'bin/Release/net10.0/NonSite.dll')
    if (-not $identity.Symbols -or $identity.PdbPath.StartsWith('/_/source')) { throw "Non-site compilation was changed: $mode" }
}

# Start watch after a Release publish and verify the normal Debug development path.
$watchLog = Join-Path $RunRoot 'mode-watch.log'
$watchError = Join-Path $RunRoot 'mode-watch-error.log'
$oldUrls = $env:ASPNETCORE_URLS
$oldBrowser = $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER
$oldPackages = $env:NUGET_PACKAGES
$env:ASPNETCORE_URLS = 'http://127.0.0.1:0'
$env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = '1'
$env:NUGET_PACKAGES = Join-Path $RunRoot 'packages'
$watchProcess = $null
try {
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = @('watch', '--project', "`"$Site`"", '--non-interactive')
        PassThru = $true
        RedirectStandardOutput = $watchLog
        RedirectStandardError = $watchError
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $watchProcess = Start-Process @start
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        Start-Sleep -Milliseconds 1000
        if ($watchProcess.HasExited) { throw 'Watch exited before serving the site.' }
        $log = Get-Content -LiteralPath $watchLog -Raw
        if ($log -match 'http://127\.0\.0\.1:(\d+)') {
            $address = $Matches[0]
            try {
                $response = Invoke-WebRequest ($address + '/first.md/') -TimeoutSec 2
                if ($response.StatusCode -eq 200 -and $response.Content.Contains('Stable body')) { $ready = $true; break }
            } catch { }
        }
    }
    if (-not $ready) { throw 'Watch did not serve the expected article.' }
    $identity = Read-Identity (Join-Path $Site 'bin/Debug/net10.0/Portable.dll')
    if (-not $identity.Symbols -or $identity.PdbPath.StartsWith('/_/source')) { throw 'Watch did not retain Debug symbols and paths.' }
    $identity | ConvertTo-Json | Set-Content (Join-Path $RunRoot 'mode-watch-identity.json')
} finally {
    if ($null -ne $watchProcess -and -not $watchProcess.HasExited) {
        if ($IsWindows) {
            & taskkill /PID $watchProcess.Id /T /F > (Join-Path $RunRoot 'mode-watch-stop.log') 2>&1
            if ($LASTEXITCODE) { throw 'Failed to stop the verification watch process.' }
        } else { $watchProcess.Kill($true) }
        $watchProcess.WaitForExit()
    }
    $env:ASPNETCORE_URLS = $oldUrls
    $env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = $oldBrowser
    $env:NUGET_PACKAGES = $oldPackages
}
Write-Output 'Release/Debug build, publish, library/test, and watch modes passed.'
