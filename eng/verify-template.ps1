# End-to-end check for the dotnet new template.
#
# This cannot be a unit test: a freshly scaffolded site references the Kiji version
# being built, which is not on nuget.org. So pack both packages, serve them from a
# local feed, scaffold a site against them, and build and run it.

[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "kiji-template-verify-$([guid]::NewGuid().ToString('N'))"
$installed = $false

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

try {
    Invoke-Step 'pack' { dotnet pack $repoRoot -c $Configuration -o $artifacts }

    $templatePackage = Get-ChildItem $artifacts -Filter 'Kiji.Templates.*.nupkg' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $templatePackage) {
        throw "No Kiji.Templates package found in $artifacts"
    }
    Write-Host "    using $($templatePackage.Name)"

    Invoke-Step 'dotnet new install' { dotnet new install $templatePackage.FullName --force }
    $installed = $true

    $siteDir = Join-Path $workDir 'Sample'
    Invoke-Step 'dotnet new kiji' {
        dotnet new kiji -o $siteDir --siteName 'Sample Site' --baseUrl 'https://example.com/sample/'
    }

    # The scaffolded site references the Kiji version just packed, so point it at the
    # local feed. A real user restores this from nuget.org.
    Set-Content -Path (Join-Path $siteDir 'NuGet.config') -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@

    # Proves version substitution, the CPM stoppers, and that .razor compiles against Kiji.
    Invoke-Step 'dotnet build (scaffolded site)' { dotnet build $siteDir -c $Configuration }
    Invoke-Step 'dotnet run -- build' { dotnet run --project $siteDir -c $Configuration --no-build -- build }

    $expected = @(
        'dist/index.html',
        'dist/hello-world/index.html',
        'dist/404.html',
        'dist/sitemap.xml',
        'dist/css/app.css'
    )
    foreach ($relativePath in $expected) {
        $path = Join-Path $siteDir ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path $path)) {
            throw "Expected generated file is missing: $relativePath"
        }
    }

    # The base path passed to the template must reach the generated markup.
    $homeHtml = Get-Content (Join-Path $siteDir 'dist/index.html') -Raw
    if ($homeHtml -notmatch [regex]::Escape('href="/sample/css/app.css"')) {
        throw 'Generated home page does not reference the stylesheet under the base path.'
    }
    if ($homeHtml -match '<base') {
        throw 'Generated page contains a <base> element, which would break page-bundle image URLs.'
    }

    # The defaults are what most users get, so check they substitute too. No build:
    # the expensive path is already covered above.
    $defaultsDir = Join-Path $workDir 'Defaults'
    Invoke-Step 'dotnet new kiji (defaults)' { dotnet new kiji -o $defaultsDir }

    $program = Get-Content (Join-Path $defaultsDir 'Program.cs') -Raw
    foreach ($token in @('SITE_NAME', 'SITE_BASE_URL')) {
        if ($program -match $token) {
            throw "Scaffolded Program.cs still contains the unsubstituted token '$token'."
        }
    }
    if (-not (Test-Path (Join-Path $defaultsDir 'Defaults.csproj'))) {
        throw 'The project file was not renamed to match the output directory.'
    }

    Write-Host ''
    Write-Host 'Template verification passed.' -ForegroundColor Green
}
finally {
    if ($installed) {
        dotnet new uninstall Kiji.Templates 2>&1 | Out-Null
    }
    if (Test-Path $workDir) {
        Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
