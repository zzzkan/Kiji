param(
    [Parameter(Mandatory)][string] $BaselinePackage,
    [Parameter(Mandatory)][string] $CandidatePackage,
    [Parameter(Mandatory)][string] $CorpusWriter,
    [string] $Repository = (Join-Path $PSScriptRoot '../../../..'),
    [string] $OutputDirectory,
    [int] $Pages = 1000,
    [ValidateRange(3, 30)][int] $Rounds = 5,
    [switch] $SkipInstrumentationCheck,
    [hashtable] $BaselineProperties = @{},
    [hashtable] $CandidateProperties = @{},
    [switch] $AllowCompressionDifferences,
    [switch] $AllowNewFingerprintAliases,
    [ValidateSet('NoChange', 'ForceRender', 'OneEdited', 'PublishOutputRemoved', 'Fresh')]
    [string[]] $Scenarios = @('NoChange', 'ForceRender', 'OneEdited', 'PublishOutputRemoved', 'Fresh')
)
$ErrorActionPreference = 'Stop'
$Repository = [IO.Path]::GetFullPath($Repository)
if (!$OutputDirectory) { $OutputDirectory = Join-Path $Repository ('artifacts/publish-profile/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Choose a new output directory; previous evidence is never overwritten.' }
New-Item -ItemType Directory -Path $root | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'harness.ps1')
$ownedPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$dotnet = (Get-Command dotnet).Source
$variants = @('baseline', 'candidate')
$results = [Collections.Generic.List[object]]::new()
$checks = [Collections.Generic.List[object]]::new()

function Get-Properties([string] $variant) {
    $properties = if ($variant -eq 'baseline') { $BaselineProperties } else { $CandidateProperties }
    foreach ($key in @($properties.Keys | Sort-Object)) {
        if ($key -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "Invalid MSBuild property: $key" }
        # MSBuild treats semicolons as property separators, even in a single argv.
        $value = [string]$properties[$key]
        $value = $value.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
        "-p:${key}=$value"
    }
}

function Invoke-Dotnet([string[]] $arguments, [string] $log, [hashtable] $environment = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $start.WorkingDirectory = $Repository
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $environment.Keys) { $start.Environment[$key] = $environment[$key] }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $timer.Stop()
        [IO.File]::WriteAllText($log, $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
        if ($process.ExitCode) { throw "dotnet failed ($($process.ExitCode)): $log" }
        return $timer.Elapsed.TotalMilliseconds
    } finally { $process.Dispose() }
}

function Remove-Owned([string] $path) {
    $absolute = [IO.Path]::GetFullPath($path)
    if (!$absolute.StartsWith($ownedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup escaped the run directory: $absolute" }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}

function Get-FileStates([string] $path) {
    $states = @{}
    if (Test-Path -LiteralPath $path) {
        foreach ($file in [IO.Directory]::EnumerateFiles($path, '*', [IO.SearchOption]::AllDirectories)) {
            $info = [IO.FileInfo]::new($file)
            $states[[IO.Path]::GetRelativePath($path, $file)] = [pscustomobject]@{ Length=$info.Length; Stamp=$info.LastWriteTimeUtc.Ticks }
        }
    }
    return $states
}

function Get-WriteCount($before, $after) {
    $count = 0
    foreach ($key in $after.Keys) {
        if (!$before.ContainsKey($key) -or $before[$key].Stamp -ne $after[$key].Stamp -or $before[$key].Length -ne $after[$key].Length) { $count++ }
    }
    return $count
}

function Get-FileSha256([string] $path) {
    $stream = [IO.File]::OpenRead($path)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}

function Assert-Outputs([string] $scenario, [int] $round) {
    $baseDist = Join-Path $root 'baseline/dist'
    $candidateDist = Join-Path $root 'candidate/dist'
    $common = 0
    $differences = [Collections.Generic.List[string]]::new()
    $aliases = [Collections.Generic.List[string]]::new()
    $aliasSources = @{}
    if ($AllowNewFingerprintAliases) {
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'candidate/obj/Release/net10.0/staticwebassets.publish.json') | ConvertFrom-Json
        foreach ($endpoint in $manifest.Endpoints) {
            if (@($endpoint.Selectors).Count) { continue }
            $label = @($endpoint.EndpointProperties | Where-Object Name -EQ 'label')
            $fingerprint = @($endpoint.EndpointProperties | Where-Object Name -EQ 'fingerprint')
            if ($label.Count -eq 1 -and $fingerprint.Count -eq 1) { $aliasSources[$endpoint.Route.Replace('/', [IO.Path]::DirectorySeparatorChar)] = $label[0].Value }
        }
    }
    foreach ($source in [IO.Directory]::EnumerateFiles($baseDist, '*', [IO.SearchOption]::AllDirectories)) {
        $relative = [IO.Path]::GetRelativePath($baseDist, $source)
        $target = Join-Path $candidateDist $relative
        if ($AllowCompressionDifferences -and [IO.Path]::GetExtension($source) -in @('.gz','.br')) {
            if (!(Test-Path -LiteralPath $target) -or (Get-FileSha256 $source) -ne (Get-FileSha256 $target)) { $differences.Add($relative) }
            continue
        }
        if (!(Test-Path -LiteralPath $target) -or (Get-FileSha256 $source) -ne (Get-FileSha256 $target)) { throw "Public output mismatch: $relative" }
        $common++
    }
    $compressedCount = 0
    foreach ($target in [IO.Directory]::EnumerateFiles($candidateDist, '*', [IO.SearchOption]::AllDirectories)) {
        $relative = [IO.Path]::GetRelativePath($candidateDist, $target)
        if ($AllowCompressionDifferences -and [IO.Path]::GetExtension($target) -in @('.gz','.br')) { continue }
        if (!(Test-Path -LiteralPath (Join-Path $baseDist $relative))) {
            if (!$AllowNewFingerprintAliases -or !$aliasSources.ContainsKey($relative)) { throw "Unexpected candidate output: $relative" }
            $original = Join-Path $baseDist $aliasSources[$relative]
            if (!(Test-Path -LiteralPath $original) -or (Get-FileSha256 $target) -ne (Get-FileSha256 $original)) { throw "New fingerprint alias does not match baseline bytes: $relative" }
            $aliases.Add($relative)
        }
    }
    # Every retained representation must decode to the actual published bytes.
    foreach ($compressed in [IO.Directory]::EnumerateFiles($candidateDist, '*', [IO.SearchOption]::AllDirectories)) {
        $extension = [IO.Path]::GetExtension($compressed)
        if ($extension -notin @('.gz', '.br')) { continue }
        $original = $compressed.Substring(0, $compressed.Length - $extension.Length)
        $stream = [IO.File]::OpenRead($compressed)
        try {
            $decoder = if ($extension -eq '.br') { [IO.Compression.BrotliStream]::new($stream, [IO.Compression.CompressionMode]::Decompress) }
                else { [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress) }
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($decoder)) }
            finally { $decoder.Dispose() }
            if ($hash -ne (Get-FileSha256 $original)) { throw "Compressed output mismatch: $compressed" }
            $compressedCount++
        } finally { $stream.Dispose() }
    }
    $checks.Add([pscustomobject]@{ Scenario=$scenario; Round=$round; CommonFiles=$common; CompressedFiles=$compressedCount; CompressionDifferences=@($differences); NewFingerprintAliases=@($aliases) })
    Write-Output "Verified $scenario/${round}: $common common files, $compressedCount decoded compressed files."
}

# Compile the measurement logger separately from every timed operation. Framework
# references come from the executing SDK; no NuGet logger dependency is introduced.
$loggerRoot = Join-Path $root 'logger'
New-Item -ItemType Directory -Path $loggerRoot | Out-Null
'<Project />' | Set-Content -LiteralPath (Join-Path $loggerRoot 'Directory.Build.props')
'<Project />' | Set-Content -LiteralPath (Join-Path $loggerRoot 'Directory.Build.targets')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><Reference Include="Microsoft.Build.Framework" HintPath="$(MSBuildBinPath)/Microsoft.Build.Framework.dll" Private="false" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $loggerRoot 'PublishTiming.csproj')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PublishTimingLogger.cs') -Destination $loggerRoot
$null = Invoke-Dotnet @('build', $loggerRoot, '-c', 'Release') (Join-Path $root 'logger-build.log')
$loggerDll = Join-Path $loggerRoot 'bin/Release/net10.0/PublishTiming.dll'

$corpus = Join-Path $root 'corpus'
$null = Invoke-Dotnet @($CorpusWriter, '--pages', "$Pages", '--runs', '1', '--full', '--root', $corpus) (Join-Path $root 'corpus.log')
if (@([IO.Directory]::GetFiles((Join-Path $corpus 'contents'), '*.md', [IO.SearchOption]::AllDirectories)).Count -ne $Pages) { throw 'Unexpected corpus page count.' }
$provenance = @{}
foreach ($variant in $variants) {
    $site = Join-Path $root $variant
    New-Item -ItemType Directory -Path $site | Out-Null
    foreach ($directory in @('contents', 'wwwroot')) { Copy-Item -LiteralPath (Join-Path $corpus $directory) -Destination (Join-Path $site $directory) -Recurse }
    $sourceRoot = Join-Path $Repository 'src/Kiji.SyntheticSite'
    foreach ($source in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -ne 'Program.cs' }) {
        $target = Join-Path $site ([IO.Path]::GetRelativePath($sourceRoot, $source.FullName))
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $target
    }
    $runner = Join-Path $site 'BuildRunner.cs'
    $code = [IO.File]::ReadAllText($runner)
    $createLine = 'StaticSite.Create([], new SiteExecutionPaths(root, Path.Combine(root, "obj", "synthetic")))'
    $publishLine = 'await app.PublishAsync(Path.Combine(root, "dist"));'
    if (!$code.Contains($createLine) -or !$code.Contains($publishLine)) { throw 'Frozen harness adapter no longer matches. Review it before measuring.' }
    [IO.File]::WriteAllText($runner, $code.Replace($createLine, 'StaticSite.Create([])').Replace($publishLine, 'await app.RunAsync();'))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PublishEntry.cs') -Destination (Join-Path $site 'Program.cs')
    '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $site 'Directory.Build.props')
    '<Project />' | Set-Content -LiteralPath (Join-Path $site 'Directory.Build.targets')
    '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $site 'Directory.Packages.props')
    $package = [IO.Path]::GetFullPath($(if ($variant -eq 'baseline') { $BaselinePackage } else { $CandidatePackage }))
    $feed = Join-Path $site 'feed'
    New-Item -ItemType Directory -Path $feed | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = @($zip.Entries | Where-Object FullName -Like '*.nuspec')[0]
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $version = $nuspec.package.metadata.version
    } finally { $zip.Dispose() }
    # Local NuGet feeds discover packages by their canonical file name, even
    # when the saved baseline artifact was renamed by the caller.
    Copy-Item -LiteralPath $package -Destination (Join-Path $feed "Kiji.$version.nupkg")
    $feedXml = [Security.SecurityElement]::Escape($feed)
    $cacheXml = [Security.SecurityElement]::Escape((Join-Path $root "packages/$variant"))
    "<configuration><packageSources><clear /><add key='local' value='$feedXml' /><add key='nuget' value='https://api.nuget.org/v3/index.json' /></packageSources></configuration>" | Set-Content -LiteralPath (Join-Path $site 'NuGet.Config')
    "<Project Sdk='Microsoft.NET.Sdk.Razor'><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>Kiji.SyntheticSite</AssemblyName><RootNamespace>Kiji.SyntheticSite</RootNamespace><RestorePackagesPath>$cacheXml</RestorePackagesPath></PropertyGroup><ItemGroup><PackageReference Include='Kiji' Version='$version' /></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $site 'Site.csproj')
    $null = Invoke-Dotnet (@('restore', $site) + @(Get-Properties $variant)) (Join-Path $root "$variant-restore.log")
    $provenance[$variant] = @{ Package=$package; SHA256=(Get-FileSha256 $package); Properties=@(Get-Properties $variant); EntrySHA256=(Get-FileSha256 (Join-Path $site 'Program.cs')); SiteDefinitionSHA256=(Get-FileSha256 $runner) }
}
if ($provenance.baseline.EntrySHA256 -ne $provenance.candidate.EntrySHA256 -or $provenance.baseline.SiteDefinitionSHA256 -ne $provenance.candidate.SiteDefinitionSHA256) { throw 'The two variants do not use the same workload.' }
$originalPost = [IO.File]::ReadAllText((Join-Path $corpus 'contents/post-00000/index.md'))

function Invoke-Publish([string] $variant, [string] $scenario, [int] $round) {
    $site = Join-Path $root $variant
    if ($scenario -eq 'Fresh') {
        foreach ($name in @('bin', 'obj', '.kiji', 'dist')) { Remove-Owned (Join-Path $site $name) }
        $null = Invoke-Dotnet (@('restore', $site) + @(Get-Properties $variant)) (Join-Path $root "$variant-$scenario-$round-restore.log")
    }
    if ($scenario -eq 'PublishOutputRemoved') { Remove-Owned (Join-Path $site 'dist') }
    if ($scenario -eq 'OneEdited') {
        $letter = if ($round % 2) { 'A' } else { 'B' }
        [IO.File]::WriteAllText((Join-Path $site 'contents/post-00000/index.md'), $originalPost + "`n`nEdit $letter.`n")
    }
    $dist = Join-Path $site 'dist'
    $compressionDir = Join-Path $site 'obj/Release/net10.0/compressed'
    $before = Get-FileStates $dist
    $compressionBefore = Get-FileStates $compressionDir
    $prefix = Join-Path $root "$variant-$scenario-$round"
    $arguments = @('publish', $site, '-c', 'Release', '--no-restore', '-v:minimal', "-logger:Kiji.Performance.PublishTimingLogger,$loggerDll;$prefix.msbuild.json")
    $arguments += @(Get-Properties $variant)
    if ($scenario -eq 'ForceRender') { $arguments += '-p:KijiForce=true' }
    $elapsed = Invoke-Dotnet $arguments ($prefix + '.log') @{ KIJI_PERFORMANCE_REPORT=($prefix + '.kiji.json') }
    $msbuild = Get-Content -Raw -LiteralPath ($prefix + '.msbuild.json') | ConvertFrom-Json
    $kiji = Get-Content -Raw -LiteralPath ($prefix + '.kiji.json') | ConvertFrom-Json
    if ($msbuild.UnmatchedEvents -ne 0) { throw "Logger missed event pairs: $prefix" }
    if ($msbuild.AccountedMilliseconds -gt $elapsed + 5) { throw "Exclusive categories exceed wall time: $prefix" }
    if ($kiji.TopLevelPhaseMilliseconds -gt $kiji.EntryMilliseconds + 1) { throw "Nested Kiji phases were double-counted: $prefix" }
    $categories = $msbuild.ExclusiveMilliseconds | ConvertTo-Json | ConvertFrom-Json -AsHashtable
    $categories['OtherPublish'] = $elapsed - $msbuild.AccountedMilliseconds
    $after = Get-FileStates $dist
    $compressionAfter = Get-FileStates $compressionDir
    $outputKinds = foreach ($kind in @('plain', '.gz', '.br')) {
        $files = @($after.Keys | Where-Object { $ext=[IO.Path]::GetExtension($_); if ($kind -eq 'plain') { $ext -notin @('.gz','.br') } else { $ext -eq $kind } })
        $bytes = 0L
        foreach ($file in $files) { $bytes += $after[$file].Length }
        [pscustomobject]@{ Kind=$kind; Count=$files.Count; Bytes=$bytes }
    }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $site 'obj/Release/net10.0/staticwebassets.publish.json') | ConvertFrom-Json
    $record = [pscustomobject]@{
        Variant=$variant; Scenario=$scenario; Round=$round; Measured=($round -gt 0); ElapsedMs=$elapsed
        Categories=$categories; Kiji=$kiji
        KijiExecMs=($msbuild.Tasks | Where-Object { $_.Name -eq 'Exec' -and $_.Target -eq 'KijiGenerateSite' } | Measure-Object Milliseconds -Sum).Sum
        CompilerInvocations=[int]($msbuild.Tasks | Where-Object Name -EQ 'Csc' | Measure-Object Count -Sum).Sum
        AppHostInvocations=[int]($msbuild.Tasks | Where-Object Name -EQ 'CreateAppHost' | Measure-Object Count -Sum).Sum
        PublishedFiles=$after.Count; PublishedWrites=(Get-WriteCount $before $after); OutputKinds=@($outputKinds)
        SdkAssets=@($manifest.Assets).Count; SdkEndpoints=@($manifest.Endpoints).Count
        CompressionFiles=$compressionAfter.Count; CompressionWrites=(Get-WriteCount $compressionBefore $compressionAfter)
    }
    $results.Add($record)
    $results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'runs.json')
    Write-Output "$variant $scenario/${round}: $([math]::Round($elapsed)) ms; Kiji entry $([math]::Round($kiji.EntryMilliseconds)) ms; published writes $($record.PublishedWrites); compression writes $($record.CompressionWrites)"
}

foreach ($variant in $variants) { Invoke-Publish $variant 'Setup' 0 }
Assert-Outputs 'Setup' 0
foreach ($scenario in $Scenarios) {
    foreach ($round in 0..$Rounds) {
        $order = if ($round % 2) { $variants } else { @('candidate', 'baseline') }
        foreach ($variant in $order) { Invoke-Publish $variant $scenario $round }
        Assert-Outputs $scenario $round
    }
}

$instrumentation = [Collections.Generic.List[object]]::new()
if (!$SkipInstrumentationCheck) {
    # Same compiled adapter and site state. Only the logger and observer/report
    # switch change. Do not subtract this estimate from the measured breakdown.
    foreach ($variant in $variants) {
        foreach ($round in 0..3) {
            $order = if ($round % 2) { @($false, $true) } else { @($true, $false) }
            foreach ($enabled in $order) {
                $label = if ($enabled) { 'on' } else { 'off' }
                $prefix = Join-Path $root "$variant-instrumentation-$label-$round"
                $arguments = @('publish', (Join-Path $root $variant), '-c', 'Release', '--no-restore', '-v:minimal')
                $arguments += @(Get-Properties $variant)
                $environment = @{ KIJI_PERFORMANCE_REPORT='' }
                if ($enabled) {
                    $arguments += "-logger:Kiji.Performance.PublishTimingLogger,$loggerDll;$prefix.msbuild.json"
                    $environment.KIJI_PERFORMANCE_REPORT = $prefix + '.kiji.json'
                }
                $elapsed = Invoke-Dotnet $arguments ($prefix + '.log') $environment
                $instrumentation.Add([pscustomobject]@{ Variant=$variant; Round=$round; Measured=($round -gt 0); Enabled=$enabled; ElapsedMs=$elapsed })
                Write-Output "$variant instrumentation $label/${round}: $([math]::Round($elapsed)) ms"
            }
        }
    }
    Assert-Outputs 'InstrumentationCheck' 0
}

$summary = foreach ($scenario in $Scenarios) {
    foreach ($variant in $variants) {
        $runs = @($results | Where-Object { $_.Variant -eq $variant -and $_.Scenario -eq $scenario -and $_.Measured })
        $sorted = @($runs.ElapsedMs | Sort-Object)
        $meanCategories = @{}
        foreach ($name in @($runs | ForEach-Object { $_.Categories.Keys } | Sort-Object -Unique)) {
            $meanCategories[$name] = ($runs | ForEach-Object { [double] $_.Categories[$name] } | Measure-Object -Average).Average
        }
        $phaseMeans = @{}
        foreach ($name in @($runs.Kiji.Phases.Phase | Sort-Object -Unique)) {
            $phaseMeans[$name] = ($runs | ForEach-Object { @($_.Kiji.Phases | Where-Object Phase -EQ $name | Measure-Object ElapsedMs -Sum).Sum } | Measure-Object -Average).Average
        }
        [pscustomobject]@{
            Scenario=$scenario; Variant=$variant; MedianMs=$sorted[[int][math]::Floor($sorted.Count/2)]
            MeanMs=($runs.ElapsedMs | Measure-Object -Average).Average
            MinMs=$sorted[0]; MaxMs=$sorted[-1]; MeanCategories=$meanCategories; MeanKijiPhases=$phaseMeans
            MeanKijiEntryMs=($runs.Kiji.EntryMilliseconds | Measure-Object -Average).Average
            MeanKijiExecMs=($runs.KijiExecMs | Measure-Object -Average).Average
            MeanCompilerInvocations=($runs.CompilerInvocations | Measure-Object -Average).Average
            MeanAppHostInvocations=($runs.AppHostInvocations | Measure-Object -Average).Average
            MeanPublishedWrites=($runs.PublishedWrites | Measure-Object -Average).Average
            MeanCompressionWrites=($runs.CompressionWrites | Measure-Object -Average).Average
            OutputKinds=$runs[-1].OutputKinds; SdkAssets=$runs[-1].SdkAssets; SdkEndpoints=$runs[-1].SdkEndpoints
        }
    }
}
[pscustomobject]@{
    CreatedUtc=[DateTime]::UtcNow; Pages=$Pages; MeasuredRounds=$Rounds; WarmupRoundsPerScenario=1
    AllowCompressionDifferences=[bool]$AllowCompressionDifferences
    AllowNewFingerprintAliases=[bool]$AllowNewFingerprintAliases
    SDK=(& $dotnet --version | Out-String).Trim(); OS=[Runtime.InteropServices.RuntimeInformation]::OSDescription
    LogicalProcessors=[Environment]::ProcessorCount; ProcessorIdentifier=$env:PROCESSOR_IDENTIFIER; Provenance=$provenance
    Semantics=@{
        NoChange='Previously built site and existing published output'
        ForceRender='KijiForce=true; existing Kiji cache, staging and published files retained'
        OneEdited='Alternating equal-length edits to exactly one Markdown input'
        PublishOutputRemoved='Only dist removed; bin/obj and Kiji cache/staging remain'
        Fresh='bin/obj/.kiji/dist removed each invocation; restore outside timing; warm OS/package caches'
        Breakdown='Exclusive event interval coverage; means add to mean whole publish. Individual task/target totals are inclusive and must not be added.'
        Kiji='Entry spans site construction, RunAsync and disposal; top-level phase sum excludes dotted nested phases'
        Writes='Counts inferred from length/last-write changes, not OS-level I/O tracing'
    }
    Summary=@($summary); OutputChecks=@($checks); InstrumentationCheck=@($instrumentation)
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'summary.json')
$summary | Select-Object Scenario, Variant, MedianMs, MeanKijiEntryMs, MeanPublishedWrites, MeanCompressionWrites | Format-Table
Write-Output "Report: $(Join-Path $root 'summary.json')"
