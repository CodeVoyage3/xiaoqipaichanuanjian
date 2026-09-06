param(
    [Parameter(Mandatory)][ValidateSet('SchemaMigrationStarted','SchemaMigrationAppliedBeforeAck','AckPersistedBeforeCandidateCommitted','SchemaSnapshotRestoreDuringCopy','OldAppRestoredBeforeOldHealthAck')][string]$Checkpoint,
    [Parameter(Mandatory)][string]$RealOldPublish,
    [Parameter(Mandatory)][string]$ResultDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$marker = Join-Path $ResultDirectory 'marker.json'
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null
if (Test-Path $marker) { throw 'fresh result directory required' }
$env:S9_T07_REAL_OLD_PUBLISH = [IO.Path]::GetFullPath($RealOldPublish)
$env:S9_T07_HARD_KILL_CHECKPOINT = $Checkpoint
$env:S9_T07_HARD_KILL_MARKER = $marker
if ($Checkpoint -eq 'AckPersistedBeforeCandidateCommitted') { $env:S9_T07_HARD_KILL_SUCCESS = '1' } else { Remove-Item Env:S9_T07_HARD_KILL_SUCCESS -ErrorAction Ignore }
$testDll = Join-Path $root 'tests\StoreExpiryInspector.Tests\bin\Release\net10.0-windows\StoreExpiryInspector.Tests.dll'
$info = [Diagnostics.ProcessStartInfo]::new('dotnet')
$info.UseShellExecute = $false; $info.WorkingDirectory = $root; $info.Arguments = ('test "{0}" --no-build --filter "FullyQualifiedName=StoreExpiryInspector.Tests.S9T07SchemaUpgradeSnapshotTests.RealProductionOldRollbackRestoresSchema9AndLoadsOldShell"' -f $testDll)
$test = [Diagnostics.Process]::Start($info)
$until = [DateTime]::UtcNow.AddSeconds(50)
while (!(Test-Path $marker) -and [DateTime]::UtcNow -lt $until -and !$test.HasExited) { Start-Sleep -Milliseconds 50 }
if (!(Test-Path $marker)) { throw "marker was not persisted: $Checkpoint" }
$m = Get-Content -Raw $marker | ConvertFrom-Json
if ($m.checkpoint -ne $Checkpoint -or [string]::IsNullOrWhiteSpace($m.operationId) -or $m.pid -le 0 -or [string]::IsNullOrWhiteSpace($m.startedUtc) -or [string]::IsNullOrWhiteSpace($m.journalPhase) -or [string]::IsNullOrWhiteSpace($m.schemaPhase)) { throw 'invalid hard-kill marker' }
$journalPath = Join-Path $m.dataRoot ("updates\{0}\journal.json" -f $m.operationId)
if (!(Test-Path $journalPath)) { throw 'marker operation journal missing' }
$journal = Get-Content -Raw $journalPath | ConvertFrom-Json
$exe = if ($m.actor -eq 'updater') { Join-Path $root 'src\StoreExpiryInspector.Updater\bin\Release\net10.0\win-x64\s9t05test\net10.0\win-x64\StoreExpiryInspector.Updater.exe' } else { Join-Path $journal.AppPath 'StoreExpiryInspector.exe' }
$actor = Get-Process -Id $m.pid -ErrorAction Stop
if ([math]::Abs(($actor.StartTime.ToUniversalTime() - ([DateTimeOffset]$m.startedUtc).UtcDateTime).TotalSeconds) -gt 1 -or [IO.Path]::GetFullPath($actor.MainModule.FileName) -ne [IO.Path]::GetFullPath($exe)) { throw 'marker process identity mismatch' }
$actorStartedIso = ([DateTimeOffset]$m.startedUtc).ToString('O',[Globalization.CultureInfo]::InvariantCulture)
$writerPid = [int]$m.writerPid; $writer = $null
if ($writerPid -ne $m.pid)
{
    $writer = Get-Process -Id $writerPid -ErrorAction Stop
    $writerStarted = [DateTimeOffset]$m.writerStartedUtc
    $updaterExe = Join-Path $root 'src\StoreExpiryInspector.Updater\bin\Release\net10.0\win-x64\s9t05test\net10.0\win-x64\StoreExpiryInspector.Updater.exe'
    if ([math]::Abs(($writer.StartTime.ToUniversalTime() - $writerStarted.UtcDateTime).TotalSeconds) -gt 1 -or [IO.Path]::GetFullPath($writer.MainModule.FileName) -ne [IO.Path]::GetFullPath($updaterExe)) { throw 'marker writer identity mismatch' }
}
$killer = Join-Path $root 'tests\StoreExpiryInspector.S9T07Fixture\bin\Release\net10.0-windows\fixture-run\StoreExpiryInspector.exe'
$killInfo = [Diagnostics.ProcessStartInfo]::new($killer); $killInfo.UseShellExecute = $false; $killInfo.Arguments = ('--s9-t07-kill {0} "{1}" "{2}"' -f $m.pid,$actorStartedIso,$exe)
$kill = [Diagnostics.Process]::Start($killInfo); if (!$kill.WaitForExit(10000) -or $kill.ExitCode -ne 0 -or !$actor.WaitForExit(5000)) { throw 'hard-killed actor did not exit' }
if ($writerPid -ne $m.pid)
{
    if (!$writer.WaitForExit(45000)) { throw 'marker writer did not exit exactly' }
    if ($Checkpoint -eq 'OldAppRestoredBeforeOldHealthAck') { $reset = Get-Content -Raw $journalPath | ConvertFrom-Json; if ($reset.Phase -ne 13 -or $reset.Schema.Phase -ne 14 -or $reset.CandidatePid -ne 0 -or $reset.Schema.CandidatePid -ne 0) { throw 'old verifier reset was not durable' } }
}
$testExited = $test.WaitForExit(60000)
if (!$testExited) { throw 'test runner did not exit within bound' }
$testExitCode = $test.ExitCode
$terminal = (Get-Content -Raw $journalPath | ConvertFrom-Json).Phase
$operationRoot = Split-Path -Parent $journalPath
$normalRecord = Join-Path $operationRoot 'normal-process.txt'
if ($terminal -notin @(10,15)) {
    $updaterExe = Join-Path $root 'src\StoreExpiryInspector.Updater\bin\Release\net10.0\win-x64\s9t05test\net10.0\win-x64\StoreExpiryInspector.Updater.exe'
    $recovery = [Diagnostics.ProcessStartInfo]::new($updaterExe); $recovery.UseShellExecute = $false; $recovery.Arguments = ('--journal "{0}"' -f $journalPath); $recovery.Environment.Remove('S9_T07_HARD_KILL_CHECKPOINT'); $recovery.Environment.Remove('S9_T07_HARD_KILL_MARKER'); $recovery.Environment['S9_T07_NORMAL_PROCESS_RECORD'] = $normalRecord
    $worker = [Diagnostics.Process]::Start($recovery); if (!$worker.WaitForExit(60000)) { throw 'recovery updater timeout' }; [IO.File]::WriteAllText((Join-Path $ResultDirectory 'recovery-exit.json'),(@{ exitCode=$worker.ExitCode; exited=$true } | ConvertTo-Json)); if ($worker.ExitCode -ne 0) { throw "recovery updater failed with exit code $($worker.ExitCode)" }
}
$final = Get-Content -Raw $journalPath | ConvertFrom-Json
if ($final.Phase -notin @(10,15)) { throw "non-terminal recovery phase: $($final.Phase)" }
if (!(Test-Path $normalRecord)) { throw 'terminal recovery did not record the normal application identity' }
$normalParts = (Get-Content -Raw $normalRecord).Trim().Split('|')
$normalPid = 0
if ($normalParts.Count -ne 2 -or ![int]::TryParse($normalParts[0], [ref]$normalPid)) { throw 'normal application identity is invalid' }
$normalStarted = [DateTimeOffset]$normalParts[1]; $normalExe = Join-Path $final.AppPath 'StoreExpiryInspector.exe'
$normalLaunch = Get-Content -Raw (Join-Path $operationRoot 'normal-launch.json') | ConvertFrom-Json
if ($normalLaunch.State -ne 2 -or $normalLaunch.pid -ne $normalPid) { throw 'normal application was not loaded' }
$ack = Get-Content -Raw (Join-Path $operationRoot 'health-ack.json') | ConvertFrom-Json
if (!$ack.uiLoaded -or $ack.integrity -ne 'ok' -or $ack.foreignKeys -ne 'ok') { throw 'health acknowledgement is invalid' }
$normal = Get-Process -Id $normalPid -ErrorAction SilentlyContinue
if ($null -ne $normal)
{
    if ([math]::Abs(($normal.StartTime.ToUniversalTime() - $normalStarted.UtcDateTime).TotalSeconds) -gt 1 -or [IO.Path]::GetFullPath($normal.MainModule.FileName) -ne [IO.Path]::GetFullPath($normalExe)) { throw 'normal application identity mismatch' }
    $normalStartedIso = $normalStarted.ToString('O',[Globalization.CultureInfo]::InvariantCulture)
    $normalKill = [Diagnostics.ProcessStartInfo]::new($killer); $normalKill.UseShellExecute = $false; $normalKill.Arguments = ('--s9-t07-kill {0} "{1}" "{2}"' -f $normalPid,$normalStartedIso,$normalExe)
    $normalKiller = [Diagnostics.Process]::Start($normalKill); if (!$normalKiller.WaitForExit(10000) -or $normalKiller.ExitCode -ne 0 -or !$normal.WaitForExit(5000)) { throw 'normal application cleanup did not exit' }
}
$summary = [ordered]@{ checkpoint=$Checkpoint; marker=$m; terminalPhase=$final.Phase; schemaPhase=$final.Schema.Phase; journal=$journalPath; normalPid=$normalPid; testExit=$testExitCode; timestampUtc=[DateTime]::UtcNow } | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'result.json'),$summary)
