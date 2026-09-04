param([Parameter(Mandatory=$true)][string]$Compiler,[string]$OutputRoot=(Join-Path $env:TEMP ([guid]::NewGuid().ToString())),[switch]$ReuseOutput)
$ErrorActionPreference='Stop'
if(!$ReuseOutput){ & $PSScriptRoot\S9T02-BuildInstaller.ps1 -Compiler $Compiler -OutputRoot $OutputRoot -TestMode }
$identity = Get-Content -Raw (Join-Path $OutputRoot 's9-t02-test-identity.json') | ConvertFrom-Json
$db = Join-Path $identity.DataRoot 'data\app.db'; $backup = Join-Path $identity.DataRoot 'backups\sentinel.txt'
function Invoke-Hidden { param([string]$file,[string]$args,[string]$name)
  $p=Start-Process -FilePath $file -ArgumentList $args -PassThru -WindowStyle Hidden
  if(!$p.WaitForExit(60000)){Stop-Process -Id $p.Id -Force;throw "$name timed out"}; if($p.ExitCode -ne 0){throw "$name failed $($p.ExitCode)"}
}
function TreeHash([string]$path){ (Get-ChildItem $path -Recurse -File|Sort FullName|%{"$($_.FullName.Substring($path.Length)):$((Get-FileHash $_.FullName).Hash)"}) -join "`n" }
function RunSetup { param([string]$args) Invoke-Hidden $identity.Installer $args 'setup' }
function NewFixture { param([string]$kind)
  $env:S9T02_FIXTURE_ROOT=$identity.DataRoot; $env:S9T02_FIXTURE_KIND=$kind
  dotnet test (Join-Path $PSScriptRoot 'StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj') --no-restore --filter FullyQualifiedName~S9T02FixtureWorkerTests
  if($LASTEXITCODE -ne 0){throw "fixture $kind failed"}; Remove-Item Env:S9T02_FIXTURE_ROOT,Env:S9T02_FIXTURE_KIND
}
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(!(Test-Path (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.exe'))){throw 'A missing installed exe'}
$p=Start-Process -FilePath (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.exe') -ArgumentList ('--data-root "{0}" --allow-existing-isolated-data-root --s9-t01-smoke-exit' -f $identity.DataRoot) -Wait -PassThru -WindowStyle Hidden
if($p.ExitCode -ne 0){throw 'A installed app failed'}
if(!(Test-Path $db)){throw 'A database missing'}
if((TreeHash (Join-Path $identity.InstallRoot 'app')) -ne (TreeHash (Join-Path $OutputRoot 'publish'))){throw 'A payload hash mismatch'}
if(!(Test-Path (Join-Path $env:USERPROFILE "Desktop\$($identity.ShortcutName).lnk"))){throw 'A desktop shortcut missing'}
if((Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction Stop).PSObject.Properties[$identity.RunValueName].Value -notmatch [regex]::Escape($identity.InstallRoot)){throw 'A Run missing'}
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Set-Content $backup 'sentinel'; $before=(Get-FileHash $backup).Hash
 $dataBefore=TreeHash $identity.DataRoot
Remove-Item (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.dll')
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(!(Test-Path (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.dll'))){throw 'B repair failed'}
if($dataBefore -ne (TreeHash $identity.DataRoot)){throw 'B data changed'}
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction Stop
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction SilentlyContinue){throw 'C Run reenabled'}
$uninstaller=Join-Path $identity.InstallRoot 'unins000.exe'; $p=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru; if($p.ExitCode -ne 0){throw 'uninstall failed'}
if(!(Test-Path $backup) -or (Get-FileHash $backup).Hash -ne $before){throw 'D data changed'}
if(Test-Path $identity.InstallRoot){throw 'D program remains'}
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(!(Test-Path $db)){throw 'E data not reusable'}
$p=Start-Process -FilePath (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.exe') -ArgumentList ('--data-root "{0}" --allow-existing-isolated-data-root --s9-t01-smoke-exit' -f $identity.DataRoot) -Wait -PassThru -WindowStyle Hidden
if($p.ExitCode -ne 0){throw 'E installed app failed'}
[pscustomobject]@{result='PASS'; completed='A-E'; identity=$identity; sentinel_sha256=$before}|ConvertTo-Json|Set-Content (Join-Path $OutputRoot 's9-t02-matrix.json') -Encoding utf8
