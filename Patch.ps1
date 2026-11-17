param([ValidateSet('Apply','Restore','Inspect')][string]$Mode = 'Apply')
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AstralPartyAndroidPatch'
$adb = Join-Path $PSScriptRoot 'platform-tools\adb.exe'
if (!(Test-Path -LiteralPath $adb -PathType Leaf)) {
    $installed = Get-Command adb.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($installed) { $adb = $installed.Source }
    else { $adb = Join-Path $toolRoot 'platform-tools\adb.exe' }
}

if (!(Test-Path -LiteralPath $adb -PathType Leaf)) {
    Write-Host 'ADB is required. This installer can download Android SDK Platform-Tools from Google.'
    Write-Host 'License: https://developer.android.com/tools/releases/platform-tools#downloads'
    if ((Read-Host 'Accept the SDK license and download? [y/N]') -notmatch '^(y|yes)$') { return }
    New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
    $stage = Join-Path $toolRoot ('download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $zip = Join-Path $stage 'platform-tools.zip'
    $oldProtocol = [Net.ServicePointManager]::SecurityProtocol
    try {
        [Net.ServicePointManager]::SecurityProtocol = $oldProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile $zip -TimeoutSec 120
        Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
        $candidate = Join-Path $stage 'platform-tools\adb.exe'
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { throw 'The downloaded archive does not contain adb.exe.' }
        & $candidate version | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'ADB could not run on this PC.' }
        $destination = Split-Path -Parent $adb
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($name in @('AdbWinApi.dll', 'AdbWinUsbApi.dll', 'adb.exe')) {
            Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $candidate) $name) -Destination $destination -Force
        }
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $oldProtocol
        $resolved = (Resolve-Path -LiteralPath $stage).Path
        $parent = (Resolve-Path -LiteralPath $toolRoot).Path
        if ((Split-Path -Parent $resolved) -eq $parent -and (Split-Path -Leaf $resolved) -match '^download-[a-f0-9]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Connect one Android phone with USB debugging enabled. Unlock it and allow USB debugging when prompted.'
$deadline = (Get-Date).AddSeconds(45)
do {
    $devices = @(& $adb devices)
    if ($LASTEXITCODE -ne 0) { throw 'ADB failed to list devices.' }
    $ready = @($devices | Where-Object { $_ -match '^\S+\s+device\s*$' })
    if ($ready.Count -gt 1) { throw 'Disconnect extra devices, then run the installer again.' }
    if ($ready.Count -eq 1) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
if ($ready.Count -ne 1) { throw 'No authorized device was detected. Check the USB cable, USB debugging permission, and Windows USB driver.' }

& (Join-Path $PSScriptRoot 'Device-Probe.ps1') -AdbPath $adb -Mode $Mode
