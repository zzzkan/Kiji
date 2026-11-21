param(
    [Parameter(Mandatory)][string] $BaselinePackage,
    [Parameter(Mandatory)][string] $CandidatePackage,
    [Parameter(Mandatory)][string] $CorpusWriter,
    [int[]] $Pages = @(200, 1000),
    [ValidateRange(3, 20)][int] $Rounds = 5,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw "Use a new run directory: $root" }
New-Item -ItemType Directory $root | Out-Null
function Run-Dotnet([string[]]$arguments, [string]$log) {
    & dotnet @arguments *> $log
    if ($LASTEXITCODE) { throw "dotnet failed. See $log" }
}
function Write-File([string]$path, [string]$value) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $value)
}
Write-File (Join-Path $root 'Directory.Build.props') '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>'
Write-File (Join-Path $root 'Directory.Build.targets') '<Project />'
Write-File (Join-Path $root 'Directory.Packages.props') '<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>'
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'harness.ps1')
$sites = @()
foreach ($pageCount in $Pages) {
    $corpus = Join-Path $root "corpus-$pageCount"
    Run-Dotnet @($CorpusWriter, '--pages', "$pageCount", '--runs', '1', '--full', '--root', $corpus) (Join-Path $root "corpus-$pageCount.log")
    foreach ($variant in @('baseline', 'candidate')) {
        $site = Join-Path $root "$variant-$pageCount"
        New-Item -ItemType Directory $site | Out-Null
        foreach ($directory in @('contents', 'wwwroot')) { Copy-Item -LiteralPath (Join-Path $corpus $directory) -Destination $site -Recurse }
        $sourceRoot = Join-Path $repository 'benchmarks/Kiji.SyntheticSite'
        $harness = @('BenchReport.cs', 'BuildRunner.cs', 'HarnessOptions.cs', 'PhaseTiming.cs', 'Program.cs', 'RunResult.cs')
        foreach ($source in Get-ChildItem $sourceRoot -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $harness -notcontains $_.Name }) {
            $path = Join-Path $site ([IO.Path]::GetRelativePath($sourceRoot, $source.FullName))
            Write-File $path ([IO.File]::ReadAllText($source.FullName))
        }
        Write-File (Join-Path $site 'Program.cs') 'Console.WriteLine($"Kiji probe PID: {Environment.ProcessId}"); var builder = Kiji.StaticSite.CreateBuilder(args); Kiji.SyntheticSite.SyntheticSiteDefinition.Configure(builder); await Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(builder.Build());'
        $package = [IO.Path]::GetFullPath($(if ($variant -eq 'baseline') { $BaselinePackage } else { $CandidatePackage }))
        $archive = [IO.Compression.ZipFile]::OpenRead($package)
        try {
            $entry = @($archive.Entries | Where-Object FullName -Like '*.nuspec')[0]
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $version = $nuspec.package.metadata.version
        } finally { $archive.Dispose() }
        $feed = Join-Path $site 'feed'
        New-Item -ItemType Directory $feed | Out-Null
        Copy-Item -LiteralPath $package -Destination (Join-Path $feed "Kiji.$version.nupkg")
        Get-ChildItem -LiteralPath (Split-Path $package) -Filter "Kiji.Runtime.*.$version.nupkg" | Copy-Item -Destination $feed
        Write-File (Join-Path $site 'NuGet.Config') "<configuration><packageSources><clear/><add key=`"local`" value=`"$([Security.SecurityElement]::Escape($feed))`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>"
        $cache = [Security.SecurityElement]::Escape((Join-Path $root "packages/$variant"))
        Write-File (Join-Path $site 'Site.csproj') "<Project Sdk=`"Microsoft.NET.Sdk.Razor`"><PropertyGroup><OutputType>Exe</OutputType><UseCurrentRuntimeIdentifier>true</UseCurrentRuntimeIdentifier><AssemblyName>Kiji.SyntheticSite</AssemblyName><RestorePackagesPath>$cache</RestorePackagesPath></PropertyGroup><ItemGroup><PackageReference Include=`"Kiji`" Version=`"$version`" /></ItemGroup></Project>"
        Run-Dotnet @('build', $site, '-c', 'Debug') (Join-Path $root "$variant-$pageCount-build.log")
        $sites += [ordered]@{ Path=$site; Pages=$pageCount; Variant=$variant; Package=$package; SHA256=(Get-FileHash $package).Hash; DefinitionSHA256=(Get-FileHash (Join-Path $site 'SyntheticSiteDefinition.cs')).Hash }
    }
}
if (@($sites.DefinitionSHA256 | Select-Object -Unique).Count -ne 1) { throw 'Site definitions differ.' }
Write-File (Join-Path $root 'probe/DevServerProbe.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DevServerProbe.cs') -Destination (Join-Path $root 'probe/Program.cs')
Run-Dotnet @('build', (Join-Path $root 'probe'), '-c', 'Release') (Join-Path $root 'probe-build.log')
@{ Root=$root; Rounds=$Rounds; Sites=$sites; SDK=(& dotnet --version); LogicalProcessors=[Environment]::ProcessorCount; OS=[Environment]::OSVersion.VersionString; Processor=$env:PROCESSOR_IDENTIFIER } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'configuration.json')
& dotnet (Join-Path $root 'probe/bin/Release/net10.0/DevServerProbe.dll') (Join-Path $root 'configuration.json')
if ($LASTEXITCODE) { throw 'Development server measurement failed.' }
