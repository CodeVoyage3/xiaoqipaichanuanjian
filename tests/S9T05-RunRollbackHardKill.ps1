param([Parameter(Mandatory)] [string]$UpdaterDll, [Parameter(Mandatory)] [string]$FixtureDirectory, [Parameter(Mandatory)] [string]$DatabasePath)
$env:S9_T05_ACK_VERSION='9.9.9'
& "$PSScriptRoot\S9T05-RunHardKill.ps1" -UpdaterDll $UpdaterDll -FixtureDirectory $FixtureDirectory -DatabasePath $DatabasePath -Checkpoints @('RollbackRequired','RollbackStarted','OldAppRestored') -Iterations 3 -Rollback
Remove-Item Env:S9_T05_ACK_VERSION -ErrorAction SilentlyContinue
