[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)] [ValidateSet('start', 'doctor', 'stop')] [string] $Command,
    [Parameter(Position = 1)] [string] $Project = 'docs',
    [Parameter(Position = 2)] [string] $Url = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$state = Join-Path 'artifacts/verify' "dev-server-$(([Uri]$Url).Port)"
$record = Join-Path $state 'process.json'
$log = Join-Path $state 'watch.log'
New-Item -ItemType Directory -Force $state | Out-Null

function Get-StartTime([System.Diagnostics.Process] $process) { $process.StartTime.ToUniversalTime() }

function Get-OwnedProcess {
    if (-not (Test-Path -LiteralPath $record)) { return $null }
    $saved = Get-Content -Raw -LiteralPath $record | ConvertFrom-Json
    $process = Get-Process -Id $saved.Id -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Remove-Item -LiteralPath $record
        return $null
    }
    if ([Math]::Abs(((Get-StartTime $process) - [DateTime]::Parse($saved.StartTime).ToUniversalTime()).TotalSeconds) -ge 1) {
        Write-Warning "Process $($saved.Id) started after this dev server; it is not ours. Forgetting $state."
        Remove-Item -LiteralPath $record
        return $null
    }
    $process
}

function Get-Status {
    try { [int](Invoke-WebRequest -Uri "$Url/" -TimeoutSec 10 -SkipHttpErrorCheck).StatusCode } catch { 0 }
}

function Start-Watch {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['ASPNETCORE_URLS'] = $Url
    $startInfo.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    if ($IsWindows) {
        $startInfo.FileName = 'cmd.exe'
        foreach ($argument in @('/d', '/c', "dotnet watch --project `"$Project`" --non-interactive > `"$log`" 2>&1 < NUL")) { $startInfo.ArgumentList.Add($argument) }
    }
    else {
        $startInfo.FileName = '/bin/sh'
        foreach ($argument in @('-c', 'exec dotnet watch --project "$0" --non-interactive > "$1" 2>&1 < /dev/null', $Project, $log)) { $startInfo.ArgumentList.Add($argument) }
    }
    [System.Diagnostics.Process]::Start($startInfo)
}

switch ($Command) {
    'start' {
        if ($running = Get-OwnedProcess) { throw "Already running: process $($running.Id)." }
        if ((Get-Status) -ne 0) { throw "$Url is already answering; it belongs to another process. Pick another url." }
        $process = Start-Watch
        @{ Id = $process.Id; StartTime = (Get-StartTime $process).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $record
        for ($second = 0; $second -lt 180; $second++) {
            if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch 'Dev server started' -Quiet)) {
                Write-Output "ready: $Url (process $($process.Id), log $log)"
                return
            }
            if ($process.HasExited) { throw "The dev server exited before it was ready:`n$((Get-Content -LiteralPath $log -Tail 40) -join "`n")" }
            Start-Sleep -Seconds 1
        }
        throw "The dev server was not ready after 180 s:`n$((Get-Content -LiteralPath $log -Tail 40) -join "`n")"
    }
    'doctor' {
        if ($null -eq (Get-OwnedProcess)) { throw "Not running (no live process recorded in $state)." }
        $code = Get-Status
        if ($code -notin @(200, 301, 302, 404)) { throw "Unhealthy: GET $Url/ returned $code." }
        $errors = @(Select-String -LiteralPath $log -Pattern 'error|Unhandled exception' -CaseSensitive)
        if ($errors.Count -gt 0) { throw "Running, but the log reports errors:`n$(($errors | Select-Object -Last 10) -join "`n")" }
        Write-Output "healthy: GET $Url/ -> $code"
    }
    'stop' {
        $process = Get-OwnedProcess
        if ($null -eq $process) {
            Write-Output "nothing started from $state"
            return
        }
        $process.Kill($true)
        $process.WaitForExit()
        Remove-Item -LiteralPath $record
        Write-Output "stopped process $($process.Id)"
    }
}
