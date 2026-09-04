param([Parameter(Mandatory)][string]$UpdaterDll,[Parameter(Mandatory)][string]$FixtureDirectory,[Parameter(Mandatory)][string]$DatabasePath)
$ErrorActionPreference='Stop'; $rows=@()
foreach($phase in 'MainExited','CandidateStaged','SwitchStarted','CandidateActivated') {
  $journal=& "$PSScriptRoot\S9T05-HardKill.ps1" -UpdaterDll $UpdaterDll -FixtureDirectory $FixtureDirectory -DatabasePath $DatabasePath
  $state=Get-Content -Raw $journal|ConvertFrom-Json; $before=(Get-FileHash (Join-Path $state.dataRoot 'data\app.db') -Algorithm SHA256).Hash
  $env:S9_T05_FAIL_PHASE=$phase; & dotnet $UpdaterDll --journal $journal; $exit=$LASTEXITCODE; Remove-Item Env:S9_T05_FAIL_PHASE
  $after=Get-Content -Raw $journal|ConvertFrom-Json; $marker=Join-Path (Split-Path $journal) 'normal-launch.marker'; $until=[DateTime]::UtcNow.AddSeconds(2); while(-not(Test-Path $marker)-and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 50}
  $rows += [pscustomobject]@{phase=$phase;exit=$exit;finalPhase=$after.Phase;oldRestored=($after.Phase -eq 15);dbUnchanged=($before -eq (Get-FileHash (Join-Path $state.dataRoot 'data\app.db') -Algorithm SHA256).Hash);normalStarted=(Test-Path $marker)}
}
$rows|ConvertTo-Json
