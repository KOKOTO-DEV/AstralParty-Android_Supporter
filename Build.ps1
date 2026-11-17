param([Parameter(Mandatory=$true)][string]$RuntimeBundle,[string]$PythonPath='python')
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$buildRoot=Join-Path $taskRoot 'build'
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
& $PythonPath (Join-Path $taskRoot 'Extract-References.py') $RuntimeBundle (Join-Path $buildRoot 'references')
if($LASTEXITCODE -ne 0){throw 'Reference extraction failed; install UnityPy in this Python environment.'}
$sdkLine=(& dotnet --list-sdks | Where-Object {$_ -match '^9\.'} | Select-Object -Last 1)
if(!$sdkLine -or $sdkLine -notmatch '^(\S+) \[(.+)\]$'){throw '.NET 9 SDK required.'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
$references=Get-ChildItem (Join-Path $buildRoot 'references/*.dll') | ForEach-Object {'/reference:'+ $_.FullName}
$sources=Get-ChildItem (Join-Path $taskRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object {$_.FullName}
& dotnet $compiler /nologo /nostdlib+ /target:library /optimize+ /langversion:latest /nullable:annotations (('/out:')+(Join-Path $buildRoot 'Bootstrap.dll')) @references @sources
if($LASTEXITCODE -ne 0){throw 'Helper compilation failed'}
& dotnet build (Join-Path $taskRoot 'builder/AndroidBatchBuilder.csproj') --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Builder compilation failed'}
& dotnet (Join-Path $taskRoot 'builder/bin/Debug/net9.0/AndroidBatchBuilder.dll') (Join-Path $buildRoot 'references/AstralParty.Runtime.dll') (Join-Path $buildRoot 'Bootstrap.dll') (Join-Path $buildRoot 'AstralParty.Runtime.dll') (Join-Path $buildRoot 'audit.json')
if($LASTEXITCODE -ne 0){throw 'Injection/verification failed'}
& $PythonPath (Join-Path $taskRoot 'Repack-Bundle.py') $RuntimeBundle $buildRoot
if($LASTEXITCODE -ne 0){throw 'Bundle packing failed'}
foreach ($name in @('runtime-probe.bundle','bundle-audit.json','audit.json')) {
    Copy-Item -LiteralPath (Join-Path $buildRoot $name) -Destination $taskRoot -Force
}
Write-Host 'Build and verification finished.'
