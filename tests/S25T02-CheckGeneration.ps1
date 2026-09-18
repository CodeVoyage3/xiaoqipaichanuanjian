$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$document = Get-Content -Raw (Join-Path $root 'tools/release/release-contract.json') | ConvertFrom-Json
$before = (git -c "safe.directory=$root" -C $root show '371b5c9:tools/release/release-contract.json') -join "`n" | ConvertFrom-Json
$checks = 0
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; $script:checks++ }
function Clone($value) { $value | ConvertTo-Json -Depth 30 | ConvertFrom-Json }
$identityCode = Get-Content -Raw (Join-Path $root 'src/StoreExpiryInspector.UpdateSafety/CurrentSchemaIdentity.cs')
$identity = @([regex]::Matches($identityCode, '"(\d{14}_[A-Za-z0-9_]+)"') | ForEach-Object { $_.Groups[1].Value })
Check (($identity -join "`n") -ceq ($document.compatibilityPolicy.generations[0].migrations -join "`n")) 'full generation schema must match unchanged production schema'
$oldBuilder = (git -c "safe.directory=$root" -C $root show '371b5c9:tools/release/Build-ReleaseCandidate.ps1') -join "`n"
$builder = Get-Content -Raw (Join-Path $root 'tools/release/Build-ReleaseCandidate.ps1')
$wirePattern = '(?s)\$manifest = Join-Path.*?\$gate = .SIGNATURE.'
Check ([regex]::Match($oldBuilder.Replace("`r", ''), $wirePattern).Value -ceq [regex]::Match($builder.Replace("`r", ''), $wirePattern).Value) 'manifest/package serialization must remain byte-identical code'
Check ([regex]::Match($builder, $wirePattern).Success) 'manifest wire-code comparison must not be empty'
function Probe($config, $release, $previous = $document, $mode = 'NOT_FOR_PUBLICATION') {
  $scratch = Join-Path ([IO.Path]::GetTempPath()) ('S25Generation-' + [guid]::NewGuid())
  New-Item -ItemType Directory -Path $scratch | Out-Null
  try {
    $env:S25_RELEASE_GENERATION_INPUT = Join-Path $scratch 'input.json'
    $env:S25_RELEASE_GENERATION_PROBE = Join-Path $scratch 'output.json'
    @{ document=$config; contract=$release; previousDocument=$previous; mode=$mode } | ConvertTo-Json -Depth 30 | Set-Content $env:S25_RELEASE_GENERATION_INPUT
    & pwsh -NoProfile -File (Join-Path $root 'tools/release/Build-ReleaseCandidate.ps1') -Version 0.0.0 -CandidateSha ('0' * 40)
    Check ($LASTEXITCODE -eq 0) 'Builder probe failed to execute'
    return Get-Content -Raw $env:S25_RELEASE_GENERATION_PROBE | ConvertFrom-Json
  } finally {
    Remove-Item Env:S25_RELEASE_GENERATION_INPUT,Env:S25_RELEASE_GENERATION_PROBE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratch -Recurse -Force
  }
}
function Rejected($result, [string]$reason) { Check ($result.status -eq 'FAILED' -and $result.failureReason.Contains($reason)) "expected rejection: $reason, actual: $($result | ConvertTo-Json -Compress)" }
foreach ($historical in $before.releases) {
  $retained = @($document.releases | Where-Object targetVersion -eq $historical.targetVersion)
  Check ($retained.Count -eq 1 -and ($historical | ConvertTo-Json -Depth 10 -Compress) -ceq ($retained[0] | ConvertTo-Json -Depth 10 -Compress)) 'historical entry changed'
  $result = Probe $document $retained[0] $before
  Check ($result.status -eq 'PASS' -and ($result.contract | ConvertTo-Json -Depth 10 -Compress) -ceq ($historical | ConvertTo-Json -Depth 10 -Compress)) 'historical resolver output changed'
}
foreach ($target in @('1.1.4','1.1.5','1.1.6')) {
  $previous = '1.1.' + (([Version]$target).Build - 1)
  $release = [pscustomobject]@{ targetVersion=$target; previousRelease="v$previous"; setupCompatibility=[pscustomobject]@{ setupMode='SAME_SCHEMA_SLIM'; crossSchemaAllowed=$false } }
  $result = Probe $document $release
  Check ($result.status -eq 'PASS') $result.failureReason
  Check ($result.contract.source.minVersion -eq '1.1.0' -and $result.contract.setupCompatibility.minimumDirectVersion -eq '1.1.0') 'both minima must inherit generation'
  Check ($result.contract.source.maxVersion -eq $previous) 'maximum must derive from previousRelease'
  Check ($result.contract.minimumProtocolVersion -eq 2 -and $result.contract.generationMigrations.Count -eq 10) 'existing protocol and full schema required'
}
$release = [pscustomobject]@{ targetVersion='1.1.4'; previousRelease='v1.1.3'; setupCompatibility=[pscustomobject]@{ setupMode='SAME_SCHEMA_SLIM'; crossSchemaAllowed=$false } }
$narrow = Clone $release
$narrow | Add-Member NoteProperty source ([pscustomobject]@{ minVersion='1.1.3' })
Rejected (Probe $document $narrow) 'narrowing'
$narrow = Clone $release
$narrow.setupCompatibility | Add-Member NoteProperty minimumDirectVersion '1.1.3'
Rejected (Probe $document $narrow) 'inherit generation'
$changed = Clone $document
$changed.compatibilityPolicy.generations[0].minimumSourceVersion = '1.1.3'
Rejected (Probe $changed $release) 'create an evidenced new generation'
Rejected (Probe $changed $release $before) 'earliest historical'
Rejected (Probe $document $release $document 'RELEASE_CANDIDATE') 'independently verified'
$newRoot = Clone $document
$rootGeneration = Clone $newRoot.compatibilityPolicy.generations[0]
$rootGeneration.id = 'G2-without-evidence'
$newRoot.compatibilityPolicy.generations += $rootGeneration
Rejected (Probe $newRoot $release) 'new generations require breaking-change/bridge evidence'
$new = Clone $document
$generation = Clone $new.compatibilityPolicy.generations[0]
$generation.id = 'G2-test'
$generation.minimumSourceVersion = '1.1.3'
$generation.breakingChange = $true
$generation | Add-Member NoteProperty previousGeneration 'G1-m10-protocol2'
$new.compatibilityPolicy.generations += $generation
$new.compatibilityPolicy.defaultGeneration = $generation.id
Rejected (Probe $new $release) 'breaking-change/bridge evidence'
$generation | Add-Member NoteProperty breakingChangeEvidence ([pscustomobject]@{ reason='synthetic breaking change'; affectedVersions='1.1.0-1.1.2'; bridge='install 1.1.3 first'; userUpgradePath='1.1.0 -> 1.1.3 -> target'; whyPreviousGenerationCannotContinue='synthetic required conversion'; reference='synthetic test evidence'; setupMinimum='1.1.3'; onlineMinimum='1.1.3' })
$result = Probe $new $release
Check ($result.status -eq 'PASS' -and $result.contract.source.minVersion -eq '1.1.3') 'evidenced new generation should pass nonpublic probe'
$missing = Clone $document
$missing.PSObject.Properties.Remove('compatibilityPolicy')
Rejected (Probe $missing $release) 'require compatibility generation'
$changed = Clone $document
$changed.releases[-1].source.minVersion = '1.1.0'
Rejected (Probe $changed $release $before) 'historical release contract changed'
Write-Output "PASS: $checks generation/Builder assertions; no build, FULL or runtime matrix executed"
