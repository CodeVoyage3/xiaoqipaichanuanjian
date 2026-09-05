$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'app\StoreExpiryInspector.exe'
if (!(Test-Path -LiteralPath $appPath -PathType Leaf)) { throw 'Extract the entire diagnostic ZIP first.' }
$diagnosticRoot = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
$diagnosticLog = Join-Path $diagnosticRoot 's9-t06-network-diagnostic.jsonl'
Write-Host 'S9-T06 GUI diagnostic candidate 1.0.2. Source 1.0.0 simulation. Prepare only; no installation.'
Write-Host 'Click the update button in the app. Then exit the app from its tray menu.'
Write-Host ('Return this JSONL: ' + $diagnosticLog)
$appArguments = '--data-root "{0}" --s9-t06-network-diagnostic "{1}" --s9-t06-prepare-only --s9-t06-simulated-source 1.0.0' -f $diagnosticRoot, $diagnosticLog
$diagnosticProcess = Start-Process -FilePath $appPath -ArgumentList $appArguments -PassThru
$diagnosticProcess.WaitForExit()
Write-Host ('JSONL: ' + $diagnosticLog)
Read-Host 'Press Enter to close'
