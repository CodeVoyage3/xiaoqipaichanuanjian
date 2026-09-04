param([string]$UpdaterDll, [string]$FixtureDirectory, [Parameter(Mandatory)] [string]$DatabasePath, [switch]$Run, [int]$ParentDelayMs = 0)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
$data = $root; $install = Join-Path $env:TEMP ([guid]::NewGuid().ToString()); $op = [guid]::NewGuid().ToString()
$parent = Start-Process powershell -ArgumentList '-NoProfile','-Command',("Start-Sleep -Milliseconds " + [Math]::Max(100, $ParentDelayMs)) -PassThru
$parentStarted = $parent.StartTime.ToUniversalTime().ToString('O')
if ($ParentDelayMs -eq 0) { $parent.WaitForExit() }
$app = Join-Path $install 'app'; $stage = Join-Path $install "app.staging-$op"; $old = Join-Path $install "app.old-$op"
New-Item -ItemType Directory -Force $app,$stage,(Join-Path $data "updates\$op") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $data 'data') | Out-Null
Copy-Item -LiteralPath $DatabasePath -Destination (Join-Path $data 'data\app.db')
foreach ($name in 'StoreExpiryInspector.exe','StoreExpiryInspector.dll','StoreExpiryInspector.deps.json','StoreExpiryInspector.runtimeconfig.json') { Copy-Item (Join-Path $FixtureDirectory $name) $app; Copy-Item (Join-Path $FixtureDirectory $name) $stage }
function Tree($p) { @(Get-ChildItem $p -File -Recurse | Sort-Object FullName | ForEach-Object { "$($_.FullName.Substring($p.Length).TrimStart('\','/'))|$($_.Length)|$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)" }) }
$oldFiles = Tree $app; $candidateFiles = Tree $stage
$sha = [Security.Cryptography.SHA256]::Create()
$oldHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($oldFiles -join "`n")))) -replace '-','')
$candidateHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($candidateFiles -join "`n")))) -replace '-','')
$sha.Dispose()
$now = [DateTimeOffset]::UtcNow.ToString('O')
$journal = @{ OperationId=$op; ProductId='StoreExpiryInspector'; InstallRoot=$install; DataRoot=$data; AppPath=$app; StagingPath=$stage; OldPath=$old; PackageSha256=(('0'*64) -join ''); SourceVersion='0.9.9'; TargetVersion='1.0.0'; ParentPid=$parent.Id; ParentStartedUtc=$parentStarted; Phase='Prepared'; OldTree=@{Files=@($oldFiles);Hash=$oldHash}; CandidateTree=@{Files=@($candidateFiles);Hash=$candidateHash}; CreatedUtc=$now; UpdatedUtc=$now; CandidatePid=0; CandidateStartedUtc=$null; LastError=$null } | ConvertTo-Json -Depth 5
$path = Join-Path $data "updates\$op\journal.json"; Set-Content $path $journal
Set-Content (Join-Path (Split-Path $path) 'candidate.zip') 'operation-package'
Write-Output $path
if (-not $Run) { return }
$result = @()
foreach ($checkpoint in 'MainExitRequested','MainExited','CandidateStaged','OldAppPreserved','SwitchStarted','CandidateActivated','CandidateStarted','WaitingForHealthAck','Committed') {
  foreach ($iteration in 1..3) {
    $env:S9_T05_CHECKPOINT = $checkpoint
    $process = Start-Process dotnet -ArgumentList $UpdaterDll,'--journal',$path -PassThru -RedirectStandardError "$root\stderr-$checkpoint-$iteration.txt"
    Start-Sleep -Milliseconds 500
    $killed = -not $process.HasExited
    if ($killed) { Stop-Process -Id $process.Id -Force }
    Remove-Item Env:S9_T05_CHECKPOINT
    $resume = & dotnet $UpdaterDll --journal $path 2>&1; $exit = $LASTEXITCODE
    $result += [pscustomobject]@{ checkpoint=$checkpoint; iteration=$iteration; killed=$killed; resumeExit=$exit; output=($resume -join "`n") }
  }
}
$result | ConvertTo-Json -Depth 4 | Set-Content "$root\s9-t05-hard-kill-result.json"
Get-Content "$root\s9-t05-hard-kill-result.json"
