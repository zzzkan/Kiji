[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Work,
    [int] $Pages = 1000,
    [int] $Rounds = 12,
    [switch] $Phases
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$program = @'
using Kiji;
using Kiji.SyntheticSite;
using Microsoft.Extensions.Hosting;

var builder = StaticSite.CreateBuilder(args);
SyntheticSiteDefinition.Configure(builder);
await builder.Build().RunAsync();

'@

$project = @'
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Kiji.SyntheticSite</AssemblyName>
    <RootNamespace>Kiji.SyntheticSite</RootNamespace>
    <UseCurrentRuntimeIdentifier>true</UseCurrentRuntimeIdentifier>
    <InvariantGlobalization>true</InvariantGlobalization>
    <InvariantTimezone>true</InvariantTimezone>
    <DefaultItemExcludes>$(DefaultItemExcludes);contents/**</DefaultItemExcludes>
    <RestorePackagesPath>__PACKAGES__</RestorePackagesPath>
    <RestoreAdditionalProjectSources>__FEED__</RestoreAdditionalProjectSources>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Kiji" Version="__VERSION__" /></ItemGroup>
</Project>

'@

$phaseHook = @'
using System;
using System.Diagnostics;
using System.Reflection;

internal static class StartupHook
{
    public static void Initialize()
    {
        var start = Process.GetCurrentProcess().StartTime;
        double Now() => (DateTime.Now - start).TotalMilliseconds;
        Console.Error.WriteLine($"KIJI_PHASE hook 0 {Now():F1}");
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name is not ("Kiji" or "Kiji.Core")) { return; }
            var observer = e.LoadedAssembly.GetType("Kiji.Generation.BuildPhaseTimer")?
                .GetProperty("Observer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            observer?.SetValue(null, new Action<string, TimeSpan>((name, elapsed) =>
                Console.Error.WriteLine($"KIJI_PHASE {name} {elapsed.TotalMilliseconds:F1} {Now():F1}")));
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Console.Error.WriteLine($"KIJI_PHASE exit 0 {Now():F1}");
            Console.Error.WriteLine($"KIJI_STAT jit-ms {System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F1}");
            Console.Error.WriteLine($"KIJI_STAT jit-methods {System.Runtime.JitInfo.GetCompiledMethodCount()}");
        };
    }
}

'@

$hookProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>KijiPhaseHook</AssemblyName></PropertyGroup>
</Project>

'@

function Invoke-Checked([string] $directory, [string[]] $arguments) {
    $output = & $arguments[0] @($arguments | Select-Object -Skip 1) 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "$($arguments -join ' ') failed in ${directory}:`n$($output.Substring([Math]::Max(0, $output.Length - 4000)))"
    }
}

function Invoke-In([string] $directory, [string[]] $arguments) {
    Push-Location $directory
    try { Invoke-Checked $directory $arguments } finally { Pop-Location }
}

function New-Package([string] $source, [string] $feed) {
    Invoke-In $source @('dotnet', 'pack', 'src/Kiji', '-c', 'Release', '-o', $feed)
    $package = Get-ChildItem $feed -Filter 'Kiji.*.nupkg' | Where-Object { -not $_.Name.StartsWith('Kiji.Runtime.') } | Select-Object -First 1
    $package.Name.Substring('Kiji.'.Length, $package.Name.Length - 'Kiji.'.Length - '.nupkg'.Length)
}

function New-Site([string] $site, [string] $repository, [string] $corpus, [string] $feed, [string] $version, [string] $packages) {
    Copy-Item (Join-Path $corpus 'contents') (Join-Path $site 'contents') -Recurse
    Copy-Item (Join-Path $corpus 'wwwroot') (Join-Path $site 'wwwroot') -Recurse
    $source = Join-Path $repository 'benchmarks/Kiji.SyntheticSite'
    foreach ($file in Get-ChildItem $source -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|Harness)[\\/]' }) {
        $target = Join-Path $site ([IO.Path]::GetRelativePath($source, $file.FullName))
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    Set-Content -LiteralPath (Join-Path $site 'Program.cs') -Value $program -NoNewline
    Set-Content -LiteralPath (Join-Path $site 'Site.csproj') -NoNewline -Value $project.Replace('__PACKAGES__', $packages).Replace('__FEED__', $feed).Replace('__VERSION__', $version)
    Invoke-In $site @('dotnet', 'publish', '-c', 'Release', '-o', 'dist')
}

function Get-BuildDirectory([string] $site, [string] $kind) {
    (Get-ChildItem (Join-Path $site "$kind/Release/net10.0") -Filter 'Kiji.SyntheticSite.dll' -Recurse -File | Select-Object -First 1).DirectoryName
}

function Get-Staging([string] $site) { Join-Path (Get-BuildDirectory $site 'obj') 'kiji' }

function Get-Phases([string[]] $lines) {
    $phases = [ordered]@{}
    $marks = @($lines | Where-Object { $_.StartsWith('KIJI_PHASE ') } | ForEach-Object {
        $parts = $_.Split(' ')
        [pscustomobject]@{ Name = $parts[1]; Duration = [double]$parts[2]; At = [double]$parts[3] }
    })
    if ($marks.Count -eq 0) { return $phases }
    foreach ($line in $lines | Where-Object { $_.StartsWith('KIJI_STAT ') }) {
        $parts = $line.Split(' ')
        $phases[$parts[1]] = [double]$parts[2]
    }
    $timed = @($marks | Where-Object { $_.Name -notin @('hook', 'exit') -and -not $_.Name.Contains('.') })
    if ($timed.Count -gt 0) {
        $phases['start'] = $timed[0].At - $timed[0].Duration
        foreach ($mark in $timed) { $phases[$mark.Name] = ($phases.Contains($mark.Name) ? $phases[$mark.Name] : 0) + $mark.Duration }
        $exits = @($marks | Where-Object Name -eq 'exit')
        if ($exits.Count -gt 0) { $phases['exit'] = $exits[-1].At - $timed[-1].At }
    }
    $phases
}

function Invoke-Generation([string] $site, [string] $hook) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($argument in @('exec', '--runtimeconfig', (Join-Path (Get-Staging $site) 'publish.runtimeconfig.json'), (Join-Path (Get-BuildDirectory $site 'bin') 'Kiji.SyntheticSite.dll'))) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.WorkingDirectory = $site
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($hook) { $startInfo.Environment['DOTNET_STARTUP_HOOKS'] = $hook }
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $timer.Stop()
    $null = $standardOutput.Result
    if ($process.ExitCode -ne 0) {
        throw "Generation failed in ${site}:`n$($standardError.Substring([Math]::Max(0, $standardError.Length - 4000)))"
    }
    $phases = Get-Phases ($standardError -split "`r?`n")
    if ($hook -and -not @($phases.Keys | Where-Object { $_ -notin @('start', 'exit') -and -not $_.StartsWith('jit-') })) {
        throw "The phase hook reported no build phases in $site. Point it at the assembly that now holds Kiji.Generation.BuildPhaseTimer."
    }
    [pscustomobject]@{ Elapsed = $timer.Elapsed.TotalMilliseconds; Phases = $phases }
}

function New-PhaseHook([string] $workDirectory) {
    $directory = Join-Path $workDirectory 'phase-hook'
    New-Item -ItemType Directory $directory | Out-Null
    Set-Content -LiteralPath (Join-Path $directory 'StartupHook.cs') -Value $phaseHook -NoNewline
    Set-Content -LiteralPath (Join-Path $directory 'KijiPhaseHook.csproj') -Value $hookProject -NoNewline
    Invoke-In $directory @('dotnet', 'build', '-c', 'Release', '-o', (Join-Path $directory 'out'))
    Join-Path $directory 'out/KijiPhaseHook.dll'
}

function Remove-Path([string] $path) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

function Initialize-Scenario([string] $site, [string] $scenario, [int] $round) {
    $post = Join-Path $site 'contents/post-00000/index.md'
    $original = Join-Path $site 'post-00000.original'
    if (-not (Test-Path -LiteralPath $original)) { Copy-Item -LiteralPath $post -Destination $original }
    $staging = Get-Staging $site
    if ($scenario -in @('fresh', 'profiled')) {
        Remove-Path (Join-Path $site '.kiji/cache')
        Remove-Path (Join-Path $staging 'site')
        if ($scenario -eq 'fresh') {
            Remove-Path (Join-Path $site '.kiji')
            Get-ChildItem $staging -Filter '*.jitprofile*' | ForEach-Object { Remove-Path $_.FullName }
            Remove-Path (Join-Path $staging 'jit-session')
        }
    }
    elseif ($scenario -eq 'one-edited') {
        $edit = if ($round % 2 -eq 0) { 'A' } else { 'B' }
        [IO.File]::WriteAllText($post, [IO.File]::ReadAllText($original) + "`n`nEdit $edit.`n")
    }
}

function Restore-Post([string] $site) {
    $original = Join-Path $site 'post-00000.original'
    if (Test-Path -LiteralPath $original) { Copy-Item -LiteralPath $original -Destination (Join-Path $site 'contents/post-00000/index.md') -Force }
}

function Get-OutputDigest([string] $site) {
    $root = Join-Path (Get-Staging $site) 'site'
    $hash = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $files = Get-ChildItem $root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } | Sort-Object -CaseSensitive
    foreach ($relative in $files) {
        $hash.AppendData([Text.Encoding]::UTF8.GetBytes($relative))
        $hash.AppendData([IO.File]::ReadAllBytes((Join-Path $root $relative)))
    }
    [Convert]::ToHexStringLower($hash.GetHashAndReset())
}

function Get-Median([double[]] $values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
}

$repository = (Get-Location).Path
$workDirectory = [IO.Path]::GetFullPath($Work)
if (Test-Path -LiteralPath $workDirectory) { throw "$workDirectory already exists; pass a new directory." }
New-Item -ItemType Directory $workDirectory | Out-Null
Set-Content -LiteralPath (Join-Path $workDirectory 'Directory.Build.props') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $workDirectory 'Directory.Build.targets') -Value '<Project />'
Set-Content -LiteralPath (Join-Path $workDirectory 'Directory.Packages.props') -Value '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>'
$baselineSource = Join-Path $workDirectory 'baseline-source'
Invoke-In $repository @('git', 'worktree', 'add', '--detach', $baselineSource, $Baseline)

$variants = [ordered]@{}
foreach ($entry in @(@{ Name = 'baseline'; Source = $baselineSource }, @{ Name = 'candidate'; Source = $repository })) {
    $feed = Join-Path $workDirectory "feed-$($entry.Name)"
    $version = New-Package $entry.Source $feed
    $variants[$entry.Name] = @{ Feed = $feed; Version = $version }
    Write-Host "$($entry.Name): Kiji $version"
}

$corpus = Join-Path $workDirectory 'corpus'
Invoke-In $repository @('dotnet', 'run', '-c', 'Release', '--project', 'benchmarks/Kiji.SyntheticSite', '--', '--pages', "$Pages", '--runs', '1', '--root', $corpus)

$sites = [ordered]@{}
foreach ($name in $variants.Keys) {
    $site = Join-Path $workDirectory "site-$name"
    New-Item -ItemType Directory $site | Out-Null
    New-Site $site $repository $corpus $variants[$name].Feed $variants[$name].Version (Join-Path $workDirectory "packages-$name")
    $sites[$name] = $site
}

if ((Get-OutputDigest $sites.baseline) -ne (Get-OutputDigest $sites.candidate)) { throw 'The two packages generate different output.' }
Write-Host 'output: identical'
$hook = if ($Phases) { New-PhaseHook $workDirectory } else { $null }

$warmup = @{ 'fresh' = 1; 'profiled' = 3; 'no-change' = 2; 'one-edited' = 2 }
Write-Host ('{0,-12}{1,10}{2,11}{3,9}   (median ms, {4} rounds)' -f 'scenario', 'baseline', 'candidate', 'change', $Rounds)
foreach ($scenario in @('fresh', 'profiled', 'no-change', 'one-edited')) {
    $samples = @{ baseline = [Collections.Generic.List[double]]::new(); candidate = [Collections.Generic.List[double]]::new() }
    $phaseSamples = @{ baseline = [ordered]@{}; candidate = [ordered]@{} }
    $previous = $null
    for ($round = 0; $round -lt $warmup[$scenario] + $Rounds; $round++) {
        $digests = @{}
        foreach ($name in $sites.Keys) {
            Initialize-Scenario $sites[$name] $scenario $round
            $result = Invoke-Generation $sites[$name] $hook
            $digests[$name] = Get-OutputDigest $sites[$name]
            if ($round -ge $warmup[$scenario]) {
                $samples[$name].Add($result.Elapsed)
                foreach ($phase in $result.Phases.Keys) {
                    if (-not $phaseSamples[$name].Contains($phase)) { $phaseSamples[$name][$phase] = [Collections.Generic.List[double]]::new() }
                    $phaseSamples[$name][$phase].Add($result.Phases[$phase])
                }
            }
        }
        if ($digests.baseline -ne $digests.candidate) { throw "The two packages generate different output ($scenario, round $($round + 1))." }
        if ($scenario -eq 'one-edited' -and $digests.baseline -eq $previous) { throw "The edit did not change the output ($scenario, round $($round + 1))." }
        $previous = $digests.baseline
    }
    foreach ($site in $sites.Values) { Restore-Post $site }
    $before = Get-Median $samples.baseline
    $after = Get-Median $samples.candidate
    Write-Host ('{0,-12}{1,10:F1}{2,11:F1}{3,8:F1}%' -f $scenario, $before, $after, (($after / $before - 1) * 100))
    foreach ($phase in $phaseSamples.candidate.Keys) {
        if ($phaseSamples.baseline.Contains($phase)) {
            Write-Host ('  {0,-10}{1,10:F1}{2,11:F1}' -f $phase, (Get-Median $phaseSamples.baseline[$phase]), (Get-Median $phaseSamples.candidate[$phase]))
        }
    }
}
