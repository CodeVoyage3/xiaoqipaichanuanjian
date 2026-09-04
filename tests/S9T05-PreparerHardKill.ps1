param([Parameter(Mandatory)] [string]$DatabasePath, [int]$Iterations = 3)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'
$rows = @()
function TreeHash($path) { $items=@(Get-ChildItem $path -File -Recurse|Sort-Object FullName|ForEach-Object { "$($_.FullName.Substring($path.Length).TrimStart('\','/'))|$($_.Length)|$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)" }); return ([BitConverter]::ToString(([Security.Cryptography.SHA256]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes(($items -join "`n"))))-replace '-','') }
foreach ($iteration in 1..$Iterations) {
    $marker = Join-Path $env:TEMP ('s9-t05-preparer-' + [guid]::NewGuid() + '.marker')
    $env:S9_T05_PREPARER_CHECKPOINT = 'StagingStarted'
    $env:S9_T05_PREPARER_MARKER = $marker
    $env:S9_T05_PREPARER_DATABASE_TEMPLATE = $DatabasePath
    $process = Start-Process dotnet -ArgumentList 'test',$project,'--no-build','--no-restore','--filter','FullyQualifiedName~InstallationPreparationCopiesIndependentUpdaterAndWritesJournalAfterRevalidation' -PassThru
    $until = [DateTime]::UtcNow.AddSeconds(20)
    while (-not (Test-Path $marker) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
    $checkpoint = if (Test-Path $marker) { Get-Content -Raw $marker | ConvertFrom-Json } else { $null }
    $staging = if ($null -ne $checkpoint) { $checkpoint.stagingPath.Trim() } else { '' }
    $database = if ($null -ne $checkpoint) { Join-Path $checkpoint.dataRoot 'data\app.db' } else { '' }
    $dbBefore = if (Test-Path $database) { (Get-FileHash $database -Algorithm SHA256).Hash } else { $null }
    $oldTreeBefore = if ($staging) { TreeHash (Join-Path (Split-Path $staging -Parent) 'app') } else { $null }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($null -ne $checkpoint) { Stop-Process -Id $checkpoint.pid -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:S9_T05_PREPARER_CHECKPOINT -ErrorAction SilentlyContinue
    Remove-Item Env:S9_T05_PREPARER_MARKER -ErrorAction SilentlyContinue
    Remove-Item Env:S9_T05_PREPARER_DATABASE_TEMPLATE -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    $install = if ($staging) { Split-Path $staging -Parent } else { '' }
    $app = Join-Path $install 'app'
    $journal = if ($null -ne $checkpoint) { Join-Path $checkpoint.operationRoot 'journal.json' } else { '' }
    $oldFile = if (Test-Path (Join-Path $app 'old.dll')) { Get-Content -Raw (Join-Path $app 'old.dll') } else { $null }
    $oldTreeAfter = if (Test-Path $app) { TreeHash $app } else { $null }
    $dbAfter = if (Test-Path $database) { (Get-FileHash $database -Algorithm SHA256).Hash } else { $null }
    $candidateRunning = if ($null -ne $checkpoint) { $null -ne (Get-Process -Id $checkpoint.pid -ErrorAction SilentlyContinue) } else { $true }
    $journalState = if (Test-Path $journal) { 'present' } else { 'absent' }
    $rows += [pscustomobject]@{ checkpoint='StagingStarted'; iteration=$iteration; killed=$true; staging=$staging; stagingExists=(Test-Path $staging); oldAppExists=(Test-Path $app); oldTreeBefore=$oldTreeBefore; oldTreeAfter=$oldTreeAfter; activeTreeHash=$oldTreeAfter; candidatePid=$checkpoint.pid; candidatePidRunning=$candidateRunning; journalState=$journalState; dbBefore=$dbBefore; dbAfter=$dbAfter; pass=([bool]$staging -and $oldTreeBefore -eq $oldTreeAfter -and $dbBefore -eq $dbAfter -and -not $candidateRunning -and -not (Test-Path $journal)) }
}
$result = Join-Path $env:TEMP ('s9-t05-preparer-hardkill-' + [guid]::NewGuid() + '.json')
$rows | ConvertTo-Json -Depth 3 | Set-Content $result
Get-Content $result
