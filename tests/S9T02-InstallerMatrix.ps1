param([Parameter(Mandatory=$true)][string]$Compiler,[string]$OutputRoot=(Join-Path $env:TEMP ([guid]::NewGuid().ToString())))
$ErrorActionPreference='Stop'
& $PSScriptRoot\S9T02-BuildInstaller.ps1 -Compiler $Compiler -OutputRoot $OutputRoot -TestMode
$identity = Get-Content -Raw (Join-Path $OutputRoot 's9-t02-test-identity.json') | ConvertFrom-Json
$db = Join-Path $identity.DataRoot 'data\app.db'; $backup = Join-Path $identity.DataRoot 'backups\sentinel.txt'
function RunSetup { param([string]$args) $p=Start-Process -FilePath $identity.Installer -ArgumentList $args -Wait -PassThru; if($p.ExitCode -ne 0){throw "setup failed $($p.ExitCode)"} }
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(!(Test-Path (Join-Path $identity.InstallRoot 'app\StoreExpiryInspector.exe'))){throw 'A missing installed exe'}
if((Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction Stop).$($identity.RunValueName) -notmatch [regex]::Escape($identity.InstallRoot)){throw 'A Run missing'}
New-Item -ItemType Directory -Force (Split-Path $backup) | Out-Null; Set-Content $backup 'sentinel'; $before=(Get-FileHash $backup).Hash
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction Stop
RunSetup '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
if(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name $identity.RunValueName -ErrorAction SilentlyContinue){throw 'C Run reenabled'}
$uninstaller=Join-Path $identity.InstallRoot 'unins000.exe'; $p=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru; if($p.ExitCode -ne 0){throw 'uninstall failed'}
if(!(Test-Path $backup) -or (Get-FileHash $backup).Hash -ne $before){throw 'D data changed'}
[pscustomobject]@{result='PASS'; completed='A-D'; identity=$identity; sentinel_sha256=$before}|ConvertTo-Json|Set-Content (Join-Path $OutputRoot 's9-t02-matrix.json') -Encoding utf8
