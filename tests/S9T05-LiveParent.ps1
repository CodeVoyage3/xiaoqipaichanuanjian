param([Parameter(Mandatory)][string]$UpdaterDll,[Parameter(Mandatory)][string]$FixtureDirectory,[Parameter(Mandatory)][string]$DatabasePath)
$ErrorActionPreference='Stop'
function Run([int]$parentDelay,[int]$wait) {
  $journal=& "$PSScriptRoot\S9T05-HardKill.ps1" -UpdaterDll $UpdaterDll -FixtureDirectory $FixtureDirectory -DatabasePath $DatabasePath -ParentDelayMs $parentDelay
  $state=Get-Content -Raw $journal|ConvertFrom-Json; $before=$state.oldTree.hash
  $env:S9_T05_PARENT_WAIT_MS=$wait; & dotnet $UpdaterDll --journal $journal; $exit=$LASTEXITCODE; Remove-Item Env:S9_T05_PARENT_WAIT_MS
  $after=Get-Content -Raw $journal|ConvertFrom-Json
  [pscustomobject]@{parentDelay=$parentDelay;wait=$wait;exit=$exit;phase=$after.Phase;oldUnchanged=($before -eq $after.oldTree.hash);appExists=(Test-Path $after.appPath);normalStarted=(Test-Path (Join-Path (Split-Path $journal) 'normal-launch.marker'))}
}
@((Run 800 3000),(Run 3000 100)) | ConvertTo-Json
