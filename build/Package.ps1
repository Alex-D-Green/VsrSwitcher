<#
.SYNOPSIS
    Builds the release files: a ZIP and, when Inno Setup is installed, the installer.

.DESCRIPTION
    Publishes VSR Switcher - the tray app, with vsrswitch.exe beside it - for win-x64, relying on
    the shared .NET 10 Desktop Runtime rather than bundling it (well under 1 MB instead of ~45 MB;
    the installer offers to install the runtime when it is missing), then packages it. The version is whatever the build stamped from the latest git tag
    (see Directory.Build.targets), read back from the published exe, so file names and installer
    always match the binaries inside them.

    Output, in artifacts\ (recreated on every run):
        VsrSwitcher-<version>-win-x64.zip
        VsrSwitcher-<version>-setup.exe    only when Inno Setup's ISCC.exe is found
        SHA256SUMS.txt
        publish\                           the folder both were made from

.PARAMETER RequireInstaller
    Fail, instead of skipping the installer, when Inno Setup is not installed. For release builds.

.EXAMPLE
    .\build\Package.ps1
#>
param([switch]$RequireInstaller)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

# Framework-dependent: the runtime is shared, installed once, and patched by Microsoft Update.
dotnet publish (Join-Path $root 'VsrSwitcher.Tray') -c Release -r win-x64 --self-contained false `
    -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md') $publish

$info = (Get-Item (Join-Path $publish 'VsrSwitcher.exe')).VersionInfo
$version = $info.ProductVersion          # 1.2.3 on a tag; 1.2.3+4.gabcdef0.dirty between tags
$fileVersion = $info.FileVersion         # always four numbers, as Windows' version resource needs
$name = 'VsrSwitcher-' + ($version -replace '\+', '-')   # '+' is awkward in file names and URLs
Write-Host "Version $version"

Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $artifacts "$name-win-x64.zip")

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "/DFileVersion=$fileVersion" "/DPublishDir=$publish" `
        "/DOutputDir=$artifacts" "/DOutputName=$name-setup" (Join-Path $root 'installer\VsrSwitcher.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
}
elseif ($RequireInstaller) {
    throw 'Inno Setup (ISCC.exe) was not found.'
}
else {
    Write-Warning 'Inno Setup is not installed, so the installer was skipped. Install it with: winget install -e --id JRSoftware.InnoSetup'
}

$sums = Get-ChildItem $artifacts -File | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
Set-Content -Path (Join-Path $artifacts 'SHA256SUMS.txt') -Value $sums -Encoding Ascii

Get-ChildItem $artifacts -File | ForEach-Object { '{0,-45} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
