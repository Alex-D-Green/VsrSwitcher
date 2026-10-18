<#
.SYNOPSIS
    Pre-release check on a real machine: an NVIDIA RTX GPU with NVIDIA App installed.

.DESCRIPTION
    Exercises vsrswitch.exe against the live driver - reads, dry runs, real writes, the
    compatibility check - and checks that the tray app starts and closes with --exit. The unit
    tests (dotnet test) cover everything that does not need this hardware.

    The only real writes flip the on-video status indicator and flip it back; at the end the
    script checks that every setting is exactly as it found it. The tray part is skipped when a
    tray is already running, so a copy you are using is never closed.

    The exit code is the number of failed checks.

.PARAMETER Folder
    The folder holding VsrSwitcher.exe and vsrswitch.exe. Defaults to the Debug build.

.EXAMPLE
    .\tests\hardware\Test-Hardware.ps1
    .\tests\hardware\Test-Hardware.ps1 -Folder .\artifacts\publish
#>
param(
    [string]$Folder = (Join-Path $PSScriptRoot '..\..\VsrSwitcher.Tray\bin\Debug\net10.0-windows')
)

$cli = Join-Path $Folder 'vsrswitch.exe'
$tray = Join-Path $Folder 'VsrSwitcher.exe'
foreach ($file in $cli, $tray) {
    if (-not (Test-Path $file)) { throw "not found: $file - build first, or pass -Folder" }
}

$script:failures = 0
$outFile = [IO.Path]::GetTempFileName()
$errFile = [IO.Path]::GetTempFileName()

# Runs vsrswitch with --json and returns exit code, parsed stdout and stderr.
function Invoke-Cli([string[]]$CommandArgs) {
    $p = Start-Process -FilePath $cli -ArgumentList ($CommandArgs + '--json') -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $stdout = [IO.File]::ReadAllText($outFile)
    $json = $null
    try { $json = $stdout | ConvertFrom-Json } catch { }
    [pscustomobject]@{ Exit = $p.ExitCode; Json = $json; Raw = $stdout; Stderr = [IO.File]::ReadAllText($errFile) }
}

# One check: the exit code, the --json contract (one object; "error" exactly when non-zero;
# nothing on stderr), and an optional condition on the parsed object.
function Test-Cli([string]$Label, [string[]]$CommandArgs, [int]$ExpectExit, [scriptblock]$Condition = { $true }) {
    $r = Invoke-Cli $CommandArgs
    $problems = @()
    if ($r.Exit -ne $ExpectExit) { $problems += "exit $($r.Exit), expected $ExpectExit" }
    if ($r.Stderr.Length -gt 0) { $problems += "stderr not empty: $($r.Stderr.Trim())" }
    if ($null -eq $r.Json) { $problems += "stdout is not one JSON object: $($r.Raw.Trim())" }
    else {
        $hasError = $null -ne $r.Json.PSObject.Properties['error']
        if ($hasError -ne ($r.Exit -ne 0)) { $problems += "'error' present=$hasError with exit $($r.Exit)" }
        if (-not (& $Condition $r.Json)) { $problems += "condition failed: $Condition" }
    }
    Write-Result $Label $problems
    return $r
}

function Write-Result([string]$Label, [string[]]$Problems) {
    if ($Problems.Count -eq 0) { Write-Host ("PASS  {0}" -f $Label) -ForegroundColor Green; return }
    $script:failures++
    Write-Host ("FAIL  {0}" -f $Label) -ForegroundColor Red
    foreach ($p in $Problems) { Write-Host "        $p" }
}

try {
    # ---- reads -------------------------------------------------------------------------------
    $initial = (Test-Cli 'status' @('status') 0 { param($j) $null -ne $j.live }).Json
    Write-Host ("      starting state: enabled={0} quality={1} indicator={2}" -f $initial.enabled, $initial.qualityLabel, $initial.indicator)

    Test-Cli 'doctor: compatible' @('doctor') 0 { param($j) $j.compatible } | Out-Null
    Test-Cli 'version' @('--version') 0 { param($j) $j.version } | Out-Null

    # ---- planning without writing ------------------------------------------------------------
    Test-Cli 'dry-run toggle writes nothing' @('-n', 'toggle') 0 {
        param($j) $j.change.dryRun -and -not $j.change.applied } | Out-Null

    $same = if ($initial.indicator) { 'on' } else { 'off' }
    $other = if ($initial.indicator) { 'off' } else { 'on' }
    Test-Cli 'asking for the current indicator is a no-op' @('indicator', $same) 0 {
        param($j) -not $j.change.applied -and $j.change.writes.Count -eq 0 } | Out-Null

    # ---- real writes, undone -----------------------------------------------------------------
    Test-Cli "indicator $other (real write)" @('indicator', $other) 0 {
        param($j) $j.change.applied -and $j.indicator -eq (-not $initial.indicator) -and $j.live.matchesRegistry } | Out-Null
    Test-Cli "indicator $same (written back)" @('indicator', $same) 0 {
        param($j) $j.change.applied -and $j.indicator -eq $initial.indicator } | Out-Null

    # ---- errors ------------------------------------------------------------------------------
    Test-Cli 'usage error' @('quality', 'bogus') 1 { param($j) $j.error.code -eq 'usage' } | Out-Null
    Test-Cli 'adapter not found' @('--adapter', '9999') 2 { param($j) $j.error.code -eq 'adapterNotFound' } | Out-Null

    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($elevated) { Write-Host 'SKIP  refresh needs elevation (running elevated, and it would blank the screen)' }
    else { Test-Cli 'refresh refuses without elevation' @('refresh') 3 { param($j) $j.error.code -eq 'accessDenied' } | Out-Null }

    # ---- the compatibility check refusing: point at the driver's trimmed nvcpl.dll -------------
    $trimmed = Join-Path $env:WINDIR 'System32\nvcpl.dll'
    if (-not (Test-Path $trimmed)) { Write-Host "SKIP  compatibility refusal ($trimmed not present)" }
    else {
        $env:VSRSWITCH_NVCPL = $trimmed
        try {
            Test-Cli 'refused: on' @('on') 5 {
                param($j) $j.error.code -eq 'incompatible' -and -not $j.compatibility.compatible } | Out-Null
            Test-Cli 'refused: dry-run on' @('-n', 'on') 5 { param($j) $j.error.code -eq 'incompatible' } | Out-Null
            Test-Cli 'refused: doctor' @('doctor') 5 {
                param($j) -not $j.compatible -and $j.error.code -eq 'incompatible' } | Out-Null
        }
        finally { Remove-Item Env:\VSRSWITCH_NVCPL }
    }

    # ---- nothing changed overall --------------------------------------------------------------
    $final = (Invoke-Cli @('status')).Json
    $changed = @()
    foreach ($name in 'enabled', 'quality', 'gpuUtilizationValue', 'indicator', 'rtxVideoFlags') {
        if ($final.$name -ne $initial.$name) { $changed += "$name was $($initial.$name), now $($final.$name)" }
    }
    Write-Result 'settings are as they were found' $changed

    # ---- the tray: starts, and closes cleanly with --exit ---------------------------------------
    if (Get-Process VsrSwitcher -ErrorAction SilentlyContinue) {
        Write-Host 'SKIP  tray start / --exit (a tray is already running; it is left alone)'
    }
    else {
        $p = Start-Process $tray -PassThru
        Start-Sleep -Seconds 2
        Write-Result 'tray starts and keeps running' $(if ($p.HasExited) { "exited with $($p.ExitCode)" } else { @() })

        $exit = Start-Process $tray -ArgumentList '--exit' -PassThru -Wait
        $problems = @()
        if ($exit.ExitCode -ne 0) { $problems += "--exit returned $($exit.ExitCode)" }
        if (-not $p.WaitForExit(5000)) { $problems += 'tray still running'; Stop-Process -Id $p.Id -Force }
        Write-Result 'tray closes with --exit' $problems

        $again = Start-Process $tray -ArgumentList '--exit' -PassThru -Wait
        Write-Result '--exit with no tray running' $(if ($again.ExitCode -ne 0) { "returned $($again.ExitCode)" } else { @() })
    }
}
finally {
    Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:failures -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green }
else { Write-Host "$($script:failures) check(s) failed." -ForegroundColor Red }
exit $script:failures
