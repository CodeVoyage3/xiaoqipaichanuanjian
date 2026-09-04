param([Parameter(Mandatory)][string]$UpdaterDll,[Parameter(Mandatory)][string]$FixtureDirectory,[Parameter(Mandatory)][string]$DatabasePath)
$journal=& "$PSScriptRoot\S9T05-HardKill.ps1" -UpdaterDll $UpdaterDll -FixtureDirectory $FixtureDirectory -DatabasePath $DatabasePath
$env:S9_T05_CHECKPOINT='MainExitRequested'
$first=$null
try {
    $first=Start-Process dotnet -ArgumentList $UpdaterDll,'--journal',$journal -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 50
        $journalText=Get-Content -LiteralPath $journal -Raw
        $atCheckpoint=$journalText -match '"Phase"\s*:\s*("MainExitRequested"|1)(?=\s*[,}])'
    } while (-not $atCheckpoint -and -not $first.HasExited -and [DateTime]::UtcNow -lt $deadline)
    $firstRunning=-not $first.HasExited
    $journalBeforeSecond=Get-Content -LiteralPath $journal -Raw
    $second=& dotnet $UpdaterDll --journal $journal 2>&1
    $secondExit=$LASTEXITCODE
    $journalAfterSecond=Get-Content -LiteralPath $journal -Raw
    $journalUnchanged=$journalBeforeSecond -ceq $journalAfterSecond
    if(-not $first.HasExited){
        Stop-Process -Id $first.Id -Force
        if(-not $first.WaitForExit(5000)){throw "first updater $($first.Id) did not stop within 5 seconds"}
    }
    Remove-Item Env:S9_T05_CHECKPOINT -ErrorAction SilentlyContinue
    $resume=& dotnet $UpdaterDll --journal $journal 2>&1
    $resumeExit=$LASTEXITCODE
    $finalJournal=Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
    $completed=($finalJournal.Phase -eq 10 -or $finalJournal.Phase -eq 'Completed')
    $finalState=if($completed){'Completed'}else{[string]$finalJournal.Phase}
    [pscustomobject]@{
        journal=$journal
        firstPid=$first.Id
        firstRunning=$firstRunning
        checkpointReached=$atCheckpoint
        secondExit=$secondExit
        journalUnchanged=$journalUnchanged
        resumeExit=$resumeExit
        finalPhase=$finalState
        pass=($firstRunning -and $atCheckpoint -and $secondExit -eq 1 -and $journalUnchanged -and $resumeExit -eq 0 -and $completed)
    }|ConvertTo-Json
} finally {
    Remove-Item Env:S9_T05_CHECKPOINT -ErrorAction SilentlyContinue
    if($null -ne $first -and -not $first.HasExited){Stop-Process -Id $first.Id -Force; [void]$first.WaitForExit(5000)}
}
