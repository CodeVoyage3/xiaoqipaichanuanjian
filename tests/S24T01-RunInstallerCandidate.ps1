param(
  [Parameter(Mandatory)][string]$CandidateRun,
  [Parameter(Mandatory)][ValidatePattern("^[0-9a-f]{40}$")][string]$ProductSourceSha,
  [Parameter(Mandatory)][string]$SourceAssets,
  [Parameter(Mandatory)][string]$ResultDirectory,
  [string]$Compiler = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$shared = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'S21T01-RunInstallerSlim.ps1'))
$start = $shared.IndexOf('$ErrorActionPreference =')
$end = $shared.IndexOf('$result = [IO.Path]::GetFullPath')
if ($start -lt 0 -or $end -le $start) { throw 'existing Slim helper boundaries changed' }
# Reuse the existing compile, identity, fingerprint and validation helpers.
# Its script-root assignment cannot resolve from a generated script block.
$helpers = $shared.Substring($start, $end - $start).Replace("`$root = (Resolve-Path (Join-Path `$PSScriptRoot '..')).Path", "`$root = '$($root.Replace("'", "''"))'")
. ([scriptblock]::Create($helpers))
function Run([string]$File, [string[]]$Arguments) {
  $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
  $process.ExitCode
}
$receipt = Get-Content -Raw (Join-Path $CandidateRun 'release-receipt.json') | ConvertFrom-Json
Require ($receipt.status -eq 'RELEASE_CANDIDATE_READY' -and $receipt.candidateSha -eq $ProductSourceSha) 'frozen candidate identity mismatch'
Require ($receipt.setupMode -eq 'SAME_SCHEMA_SLIM' -and $receipt.migrationCount -eq 10) 'same-schema identity mismatch'
Require (-not (Test-Path -LiteralPath $ResultDirectory)) 'fresh evidence root required'
New-Item -ItemType Directory -Path $ResultDirectory | Out-Null
$results = @()
foreach ($scenario in @('Fresh', 'SameSchema', 'DowngradeBlock')) {
  $result = Join-Path $ResultDirectory ([guid]::NewGuid().ToString())
  New-Item -ItemType Directory -Path $result | Out-Null
  $install = New-GuidRoot; $data = New-GuidRoot; $identity = [guid]::NewGuid().ToString()
  $database = Join-Path $data 'data\app.db'
  $candidate = Compile-Setup (Join-Path $CandidateRun 'publish') '1.1.3' '1.1.2' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'candidate') $identity
  $previous = Compile-Setup (Join-Path $SourceAssets 'publish') '1.1.2' '1.1.2' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'previous') $identity
  $summary = [ordered]@{ scenario=$scenario; evidenceKind='PRODUCTION_PAYLOAD_ISOLATED_SETUP_TEST_IDENTITY'; identity=$identity; install=$install; data=$data; productSource=$receipt.candidateSha; candidateRun=$receipt.runId; status='FAILED' }
  try {
    if ($scenario -eq 'SameSchema') {
      Require ((Install $previous (Join-Path $result 'previous-setup.log')) -eq 0) 'v1.1.2 baseline install failed'
      Assert-InstalledIdentity $install $identity '1.1.2'
      Require ((Initialize-App $install $data) -eq 0) 'v1.1.2 baseline launch failed'
      $null = Business $database 'seed' (Join-Path $result 'seed.txt')
      $summary.before = Business $database 'fingerprint' (Join-Path $result 'before.txt')
      $summary.beforeDb = Hash $database
    }
    Require ((Install $candidate (Join-Path $result 'candidate-setup.log')) -eq 0) 'v1.1.3 install failed'
    Assert-InstalledIdentity $install $identity '1.1.3'
    if ($scenario -eq 'SameSchema') { $summary.afterSetupDb = Hash $database; Require ($summary.beforeDb -eq $summary.afterSetupDb) 'Setup changed database bytes' }
    Require ((Initialize-App $install $data) -eq 0) 'v1.1.3 first launch failed'
    if ($scenario -eq 'SameSchema') { $summary.after = Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.before -eq $summary.after) 'business fingerprint changed' }
    $summary.validation = Business $database 'validate' (Join-Path $result 'validation.json')
    Require ($summary.validation.migrationCount -eq 10 -and $summary.validation.integrity -eq 'ok' -and $summary.validation.foreignKeys -eq 0) 'database gate failed'
    if ($scenario -eq 'DowngradeBlock') {
      $null = Business $database 'seed' (Join-Path $result 'seed.txt')
      $summary.beforeDb = Hash $database; $summary.beforeTree = Tree-Fingerprint $install
      $summary.downgradeExit = Install $previous (Join-Path $result 'downgrade-setup.log')
      Require ($summary.downgradeExit -ne 0) 'v1.1.3 -> v1.1.2 downgrade not blocked'
      $summary.afterDb = Hash $database; $summary.afterTree = Tree-Fingerprint $install
      Require ($summary.beforeDb -eq $summary.afterDb -and $summary.beforeTree -eq $summary.afterTree) 'blocked downgrade changed protected state'
    }
    Assert-NoUpdaterTransaction $data
    $summary.status = 'PASS'
    $results += $summary
  }
  finally { [IO.File]::WriteAllText((Join-Path $result 'result.json'), ($summary | ConvertTo-Json -Depth 12)) }
}
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'results.json'), ($results | ConvertTo-Json -Depth 12))
