param(
    [Parameter(Mandatory=$true)][string]$AdbPath,
    [string]$OriginalBundle,
    [string]$Serial,
    [ValidateSet('Inspect','Apply','Restore')][string]$Mode = 'Inspect',
    [switch]$Collect
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Android-Runtime-Catalog.ps1')
$Package = 'com.feimo.astralpartyjpn'
$ExpectedCatalog = '595c6bac9d7f64a58d2ea41f2f53b8d3999241e1775dd7f3cd5f6f0b9fcf7d4c'
$Relative = '4da1f23c112208b0f8df2a1327b4e4ce/e7b6473b5082c25f7fd2ecd1c874717d/__data'
$FilesRoot = "/storage/emulated/0/Android/data/$Package/files"
$CacheRoot = "$FilesRoot/com.unity.addressables"
$OriginalSha = '7e77ef50fd0c718904af80cb13f7fbf8d4b0d53656437579cab299e774aae96c'
$Audit = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundle-audit.json') -Raw | ConvertFrom-Json
$ProbeSha = $Audit.output_bundle_sha256
$PreviousProbeShas = @('c8cc92a875876c9db68184619e156ed1c25033fd9351eaefb3272d73a6761944','dcc544dcb47a7f809cc080438b6459c13900ec7111e61aec202afa0ff0553e1d','85085b09c4cbcaf98c2f26711a4b36dbc0eaf1de2e99b9ab9a49d03f476d6119','e47f6aaa05ca95023306bac9ca846eb123800aa5f52648dc9f4896f894dd299d','04e95bd174775c123642e5ee4c869e8bfe506473f1742298d080c9f9b8cd11b5','3fa8fa56fd098740a657c76ec65cfdbb0a45d7c16fe4108b07a50dca9cd289d7','34b4ff6a9e2fd4d7a50791cc41e02963b9532f25682b4cf630844f2aab49f817','10b913e1e27bca87b67188dd846f85014d6a21d4ca36c282de48f20da470eafe','baa77efb42261e62b2ef1687da57e3d4aa858bee2f13a674c08bf0d6aacd80f1','a14e7c2afa4b7120221d8135897a0786b406cab5fc50d56563d7b0253cba2e56','bb647c918f34188a133536decccecbd03d1edf6b0ae4a646262b64f2366857fc','b73e7f23f58cf48b47085185e6e6285ebbbc572701a5f2227c0a22feb38f18cf','61bd6b2565bd3b479f9bdcffe4f98d4a0fd1ecf9d7bf940a51033a583104b8fd','1cf0ee8c29e939ed80782304d608f475487d746c219a17ab3729ad92a282460f')
$BackupRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AstralPartyAndroidPatch'
$SavedOriginal = Join-Path $BackupRoot ($OriginalSha + '.bundle')
$Original = if ($OriginalBundle) {(Resolve-Path -LiteralPath $OriginalBundle).Path} else {$SavedOriginal}
$Probe = Join-Path $PSScriptRoot 'runtime-probe.bundle'
if (!(Test-Path -LiteralPath $AdbPath -PathType Leaf)) { throw 'Specify the full adb.exe path with -AdbPath.' }
foreach($check in @(@($Original,$OriginalSha),@($Probe,$ProbeSha))) {
    if ($check[0] -eq $Original -and !$OriginalBundle -and !(Test-Path -LiteralPath $Original)) { continue }
    if ((Get-FileHash -LiteralPath $check[0] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $check[1]) {
        throw ('Kit file hash mismatch: '+$check[0])
    }
}
function Invoke-AdbText {
    param([string[]]$AdbArguments)
    # Windows PowerShell 5.1 wraps native stderr as ErrorRecord, even on exit 0.
    # adb prints successful pull/push progress there. Judge the native exit code.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $PSNativeCommandUseErrorActionPreference = $false
        $lines = @(& $AdbPath @AdbArguments 2>&1)
        $nativeExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($nativeExitCode -ne 0) { throw ('adb failed (exit '+$nativeExitCode+'): '+($lines -join "`n")) }
    return ($lines -join "`n").Trim()
}
$devices = Invoke-AdbText -AdbArguments @('devices','-l')
$connected = @($devices -split "`r?`n" | ForEach-Object {
    if ($_ -match '^(\S+)\s+device(?:\s|$)') { $Matches[1] }
})
if (!$Serial) {
    if ($connected.Count -ne 1) { throw 'Exactly one authorized USB device is required, or specify -Serial.' }
    $Serial = $connected[0]
}
if ($Serial -notin $connected) { throw 'The selected device is not connected and authorized.' }
function Invoke-DeviceText {
    param([string[]]$DeviceArguments)
    Invoke-AdbText -AdbArguments (@('-s',$Serial)+$DeviceArguments)
}
function Get-DeviceHash {
    param([string]$RemotePath)
    $result = Invoke-DeviceText -DeviceArguments @('shell','sha256sum',$RemotePath)
    if ($result -notmatch '^([a-fA-F0-9]{64})\s') { throw 'Could not read a valid remote SHA-256.' }
    return $Matches[1].ToLowerInvariant()
}
# Select the newest complete catalog pair. Do not patch an older retained cache.
$catalogListing = Invoke-DeviceText -DeviceArguments @('shell','find',$CacheRoot,'-maxdepth','1','-type','f','-name','catalog*')
$catalogRows = @($catalogListing -split "`r?`n" | ForEach-Object {
    $name = ($_ -split '/')[-1]
    if ($name -cmatch '^catalog_((?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8}))\.(json|hash)$') {
        [pscustomobject]@{Name=$name;Version=[version]$Matches[1];Extension=$Matches[2]}
    } elseif ($name -match '\.(json|hash)$') { throw ('Unrecognized catalog name: '+$name) }
})
if (!$catalogRows.Count -or $catalogRows.Count -gt 512) { throw 'Missing or excessive catalog inventory.' }
$activeVersion = ($catalogRows | Sort-Object Version -Descending | Select-Object -First 1).Version

$activeRows = @($catalogRows | Where-Object {$_.Version -eq $activeVersion})
if ($activeRows.Count -ne 2 -or @($activeRows | Where-Object {$_.Extension -eq 'json'}).Count -ne 1 -or @($activeRows | Where-Object {$_.Extension -eq 'hash'}).Count -ne 1) {
    throw 'Newest catalog pair is incomplete. Finish the game resource download first.'
}
$catalogName = 'catalog_'+$activeVersion+'.json'
$catalogRemote = "$CacheRoot/$catalogName"
$catalog = Get-DeviceHash -RemotePath $catalogRemote
$hashRemote = "$CacheRoot/catalog_$activeVersion.hash"
$catalogHashFile = Get-DeviceHash -RemotePath $hashRemote
$evidence = Join-Path $PSScriptRoot ('device-evidence/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$catalogLocal = Join-Path $evidence $catalogName
Invoke-DeviceText -DeviceArguments @('pull',$catalogRemote,$catalogLocal) | Out-Null
if ((Get-FileHash -LiteralPath $catalogLocal -Algorithm SHA256).Hash.ToLowerInvariant() -ne $catalog) { throw 'Catalog changed while collecting. Retry after the download finishes.' }
$hashText = Invoke-DeviceText -DeviceArguments @('shell','cat',$hashRemote)
if ($hashText.Trim() -cnotmatch '^[a-fA-F0-9]{32}$') { throw 'Invalid Addressables catalog hash file.' }
$location = Get-BapRuntimeCatalog -Path $catalogLocal
$Relative = $location.Relative
$Target = "$CacheRoot/AssetBundles/$Relative"
$current = Get-DeviceHash -RemotePath $Target
# Whole APKs are conservative native/metadata pins; content-only cache updates do not change them.
$expectedApks = @{
    'base.apk'='e6cc2fc3e906ea10bf7e0b8434e85bcc7dcc18bad3b3409242cb89573b511b7d'
    'split_config.arm64_v8a.apk'='8353ea28bf2657d7f0b0e2c4dddc23ba129af62b59836bf8f385e70f4ae79450'
}
$apkPaths = Invoke-DeviceText -DeviceArguments @('shell','pm','path',$Package)
$apkRows = @($apkPaths -split "`r?`n" | ForEach-Object {
    if ($_ -notmatch '^package:(/[A-Za-z0-9_./=+~@-]+)$') { throw 'Invalid installed APK path.' }
    $remote=$Matches[1];$name=($remote -split '/')[-1]
    [pscustomobject]@{Name=$name;Path=$remote;SHA256=(Get-DeviceHash -RemotePath $remote)}
})
$nativeMatches = $apkRows.Count -eq $expectedApks.Count
$seenApks = @{}
foreach ($apk in $apkRows) {
    if ($seenApks.ContainsKey($apk.Name) -or !$expectedApks.ContainsKey($apk.Name) -or $apk.SHA256 -ne $expectedApks[$apk.Name]) { $nativeMatches=$false }
    $seenApks[$apk.Name]=$true
}
$state = if ($current -eq $OriginalSha) {'original'} elseif ($current -eq $ProbeSha) {'probe'} elseif ($current -in $PreviousProbeShas) {'previous-probe'} else {'other'}
$assetMatches = $location.RuntimeAssetId -ceq '8'
$admitted = $nativeMatches -and $assetMatches -and $state -ne 'other'
$report = [pscustomobject]@{ScriptRevision=26; ProbeRevision='AstralParty-Android-Supporter-1.0.0'; GameDataVersion=$activeVersion.ToString(); CompatibilityPolicy='Known APKs + catalog-referenced runtime bytes'; Mode=$Mode; Package=$Package; Catalog=$catalog; CatalogMatches=($catalog -eq $ExpectedCatalog); NativeMatches=$nativeMatches; RuntimeAssetMatches=$assetMatches; BundleState=$state; CurrentSHA256=$current; Target=$Target; Allowed=$admitted; Evidence=$evidence; InstalledAPKs=$apkRows}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'inspect.json') -Encoding UTF8
$report | Select-Object * -ExcludeProperty InstalledAPKs | Format-List
if ($Collect) {
    $runtimeLocal=Join-Path $evidence 'runtime-current.bundle'
    Invoke-DeviceText -DeviceArguments @('pull',$Target,$runtimeLocal) | Out-Null
    if ((Get-FileHash -LiteralPath $runtimeLocal -Algorithm SHA256).Hash.ToLowerInvariant() -ne $current -or (Get-DeviceHash -RemotePath $Target) -ne $current) { throw 'Collected runtime changed during collection.' }
    $collectedZip=$evidence+'.zip'
    Compress-Archive -LiteralPath @($catalogLocal,$runtimeLocal,(Join-Path $evidence 'inspect.json')) -DestinationPath $collectedZip
    Write-Output ('Collected current catalog/runtime ZIP: '+$collectedZip)
}
if ($Mode -eq 'Inspect') { return }
if (!$nativeMatches) { throw 'Installed APK/native baseline changed. Keep inspect.json and collect the new APKs for review.' }
if (!$assetMatches) { throw 'Runtime TextAsset mapping changed; retain collected evidence for review.' }
if ($state -eq 'other') { throw 'Catalog references a changed runtime bundle. Run -Collect, then provide the device-evidence folder for rebasing.' }
if ($catalog -ne $ExpectedCatalog) { Write-Warning 'Catalog content changed; admitted because installed APKs and the referenced runtime bytes match known baselines.' }
if (($Mode -eq 'Apply' -and $state -eq 'probe') -or ($Mode -eq 'Restore' -and $state -eq 'original')) {
    Write-Output 'The requested state is already present.'; return
}
if ($Mode -eq 'Restore' -and !(Test-Path -LiteralPath $Original -PathType Leaf)) {
    throw 'Original backup is missing. Use the original bundle from the previous kit with -OriginalBundle.'
}
$expectedCurrent = if ($state -eq 'previous-probe') {$current} elseif ($Mode -eq 'Apply') {$OriginalSha} else {$ProbeSha}
$source = if ($Mode -eq 'Apply') {$Probe} else {$Original}
$expectedResult = if ($Mode -eq 'Apply') {$ProbeSha} else {$OriginalSha}
if ($current -ne $expectedCurrent) { throw 'Unrecognized runtime bundle. Existing modifications will not be overwritten.' }
# Apply/Restore are explicit opt-in modes; this script is never run by the analysis agent.
Invoke-DeviceText -DeviceArguments @('shell','am','force-stop',$Package) | Out-Null
if ((Get-DeviceHash -RemotePath $catalogRemote) -ne $catalog -or (Get-DeviceHash -RemotePath $hashRemote) -ne $catalogHashFile) { throw 'Catalog changed during preflight.' }
if ((Invoke-DeviceText -DeviceArguments @('shell','pm','path',$Package)) -ne $apkPaths) { throw 'Installed APK paths changed during preflight.' }
foreach ($apk in $apkRows) { if ((Get-DeviceHash -RemotePath $apk.Path) -ne $apk.SHA256) { throw 'Installed APK changed during preflight.' } }
if ((Get-DeviceHash -RemotePath $Target) -ne $expectedCurrent) { throw 'Target changed during preflight.' }
$backupPath = Join-Path $PSScriptRoot ('device-before-'+$Mode.ToLowerInvariant()+'-'+$expectedCurrent+'.bundle')
Invoke-DeviceText -DeviceArguments @('pull',$Target,$backupPath) | Out-Null
if ((Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedCurrent) { throw 'Device backup verification failed.' }
if ($Mode -eq 'Apply' -and ($state -eq 'original' -or (Test-Path -LiteralPath $Original -PathType Leaf))) {
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    if (!(Test-Path -LiteralPath $SavedOriginal)) {
        $originalSource = if ($state -eq 'original') {$backupPath} else {$Original}
        Copy-Item -LiteralPath $originalSource -Destination $SavedOriginal
    }
}
$staging = '/data/local/tmp/bap-android-probe-'+[Guid]::NewGuid().ToString('N')+'.bundle'
$next = $Target+'.bap-probe-stage'
try {
    Invoke-DeviceText -DeviceArguments @('push',$source,$staging) | Out-Null
    if ((Get-DeviceHash -RemotePath $staging) -ne $expectedResult) { throw 'Uploaded file verification failed.' }
    Invoke-DeviceText -DeviceArguments @('shell','cp',$staging,$next) | Out-Null
    if ((Get-DeviceHash -RemotePath $next) -ne $expectedResult) { throw 'Destination staging verification failed.' }
    Invoke-DeviceText -DeviceArguments @('shell','mv','-f',$next,$Target) | Out-Null
    if ((Get-DeviceHash -RemotePath $Target) -ne $expectedResult) { throw 'Final verification failed. Preserve the backup and inspect the device state.' }
    Write-Output "$Mode complete. Start the game manually when ready."
} finally {
    # Exact, generated staging files only; no recursive deletion.
    try {
        Invoke-DeviceText -DeviceArguments @('shell','rm','-f',$staging,$next) | Out-Null
    } catch {
        Write-Warning ('Could not remove probe staging files: '+$_.Exception.Message)
    }
}
