param(
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][string]$CandidateSha,
  [ValidateSet('NOT_FOR_PUBLICATION','RELEASE_CANDIDATE')][string]$Mode = 'NOT_FOR_PUBLICATION',
  [string]$Compiler,
  [string]$AdvanceComp,
  [string]$SigningKeyFile,
  [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path (Join-Path $PSScriptRoot 'ReleaseZipArchive.cs')
$builderRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$CandidateSha = $CandidateSha.ToLowerInvariant()
$Compiler = if ($Compiler) { $Compiler } elseif ($env:STORE_EXPIRY_ISCC) { $env:STORE_EXPIRY_ISCC } else { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
$AdvanceComp = if ($AdvanceComp) { $AdvanceComp } elseif ($env:STORE_EXPIRY_ADVZIP) { $env:STORE_EXPIRY_ADVZIP } else { Join-Path $env:LOCALAPPDATA 'Programs\AdvanceCOMP\advzip.exe' }
$SigningKeyFile = if ($SigningKeyFile) { $SigningKeyFile } else { $env:STORE_EXPIRY_SIGNING_KEY_FILE }
$OutputRoot = if ($OutputRoot) { $OutputRoot } elseif ($env:STORE_EXPIRY_RELEASE_OUTPUT_ROOT) { $env:STORE_EXPIRY_RELEASE_OUTPUT_ROOT } else { Join-Path $env:LOCALAPPDATA 'StoreExpiryInspector.ReleaseBuilder\runs' }
$runId = [guid]::NewGuid().ToString()
$startedAt = [DateTimeOffset]::UtcNow.ToString('O')
$gate = 'INITIALIZE'
$run = $null
$receiptPath = $null
$privateKey = $null
$rsa = $null

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Native-Text([string]$File, [string[]]$Arguments, [string]$WorkingDirectory) {
  Push-Location $WorkingDirectory
  try {
    $lines = @(& $File @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit $LASTEXITCODE`n$($lines -join [Environment]::NewLine)" }
    $last = $lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1
    if ($null -eq $last) { return '' }
    return $last.ToString().Trim()
  } finally { Pop-Location }
}
function Native-Checked([string]$Name, [string]$File, [string[]]$Arguments, [string]$WorkingDirectory) {
  Push-Location $WorkingDirectory
  try { & $File @Arguments; if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit $LASTEXITCODE" } }
  finally { Pop-Location }
}
function Native-AllText([string]$Name, [string]$File, [string[]]$Arguments, [string]$WorkingDirectory) {
  Push-Location $WorkingDirectory
  try {
    $lines = @(& $File @Arguments 2>&1); $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$Name failed with exit $exitCode`n$($lines -join [Environment]::NewLine)" }
    return ($lines -join [Environment]::NewLine)
  } finally { Pop-Location }
}
function Native-CapturedText([string]$Name, [string]$File, [string[]]$Arguments, [string]$WorkingDirectory) {
  $start = [Diagnostics.ProcessStartInfo]::new($File)
  $start.WorkingDirectory = $WorkingDirectory
  $start.UseShellExecute = $false
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($argument in $Arguments) { $null = $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::Start($start)
  $stdout = $process.StandardOutput.ReadToEndAsync()
  $stderr = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $output = $stdout.GetAwaiter().GetResult()
  $errorOutput = $stderr.GetAwaiter().GetResult()
  if ($process.ExitCode -ne 0) { throw "$Name failed with exit $($process.ExitCode)`n$errorOutput" }
  return $output
}
function Sorted-Unique([string[]]$Values) {
  $set = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
  foreach ($value in @($Values)) { if (-not [string]::IsNullOrWhiteSpace($value)) { $null = $set.Add($value) } }
  return @($set)
}
function Path-Categories([string]$Path, $Policy) {
  if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Contains('\') -or $Path.StartsWith('/', [StringComparison]::Ordinal) -or
      ($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') })) { return @('UNKNOWN') }
  $exact = @($Policy.exactRules | Where-Object { [string]::Equals([string]$_.path, $Path, [StringComparison]::Ordinal) })
  if ($exact.Count -eq 1) { return @(Sorted-Unique @($exact[0].categories)) }
  if ($exact.Count -gt 1) { return @('UNKNOWN') }
  $prefixes = @($Policy.prefixRules | Where-Object { $Path.StartsWith([string]$_.prefix, [StringComparison]::Ordinal) })
  if ($prefixes.Count -gt 0) {
    $longest = ($prefixes | ForEach-Object { ([string]$_.prefix).Length } | Measure-Object -Maximum).Maximum
    return @(Sorted-Unique @($prefixes | Where-Object { ([string]$_.prefix).Length -eq $longest } | ForEach-Object { @($_.categories) }))
  }
  if ($Path.StartsWith([string]$Policy.testFallback.prefix, [StringComparison]::Ordinal) -and
      $Path.EndsWith([string]$Policy.testFallback.suffix, [StringComparison]::OrdinalIgnoreCase)) {
    return @(Sorted-Unique @($Policy.testFallback.categories))
  }
  return @('UNKNOWN')
}
function Git-ChangeEntries([string]$Repository, [string]$BaseSha, [string]$TargetSha) {
  $raw = Native-CapturedText 'change impact diff' 'git' @('-c',"safe.directory=$Repository",'-C',$Repository,'diff','--raw','-z','--find-renames','--find-copies',$BaseSha,$TargetSha,'--') $Repository
  $tokens = $raw.Split([char]0)
  $entries = @()
  for ($index = 0; $index -lt $tokens.Length -and -not [string]::IsNullOrEmpty($tokens[$index]);) {
    $header = $tokens[$index++]
    $parts = @($header.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
    Require ($parts.Count -eq 5 -and $parts[0].StartsWith(':', [StringComparison]::Ordinal)) "malformed raw diff header: $header"
    $status = $parts[4]
    Require ($status -match '^[ACDMRTUXB][0-9]*$') "unsupported raw diff status: $status"
    $entry = [ordered]@{ status=$status; oldMode=$parts[0].Substring(1); newMode=$parts[1] }
    if ($status[0] -in @('R','C')) {
      Require ($index + 1 -lt $tokens.Length) 'raw diff rename/copy paths are incomplete'
      $entry.oldPath = $tokens[$index++]; $entry.newPath = $tokens[$index++]
    } else {
      Require ($index -lt $tokens.Length) 'raw diff path is incomplete'
      $entry.path = $tokens[$index++]
    }
    $entries += [pscustomobject]$entry
  }
  return @($entries)
}
function Resolve-BaseProductSource([string]$Repository, [string]$PreviousRelease, [string]$TargetSha) {
  Require (-not [string]::IsNullOrWhiteSpace($PreviousRelease)) 'release contract previousRelease is missing'
  $tagRef = "refs/tags/$PreviousRelease"
  $tagType = Native-Text 'git' @('-c',"safe.directory=$Repository",'-C',$Repository,'cat-file','-t',$tagRef) $Repository
  Require ($tagType -eq 'tag') "previousRelease must be an annotated tag: $PreviousRelease"
  $baseSha = (Native-Text 'git' @('-c',"safe.directory=$Repository",'-C',$Repository,'rev-parse',"$tagRef^{commit}") $Repository).ToLowerInvariant()
  Require ($baseSha -match '^[0-9a-f]{40}$') "previousRelease did not peel to a full commit SHA: $PreviousRelease"
  Push-Location $Repository
  try {
    & git -c "safe.directory=$Repository" -C $Repository merge-base --is-ancestor $baseSha $TargetSha
    Require ($LASTEXITCODE -eq 0) "baseProductSource is not an ancestor of CandidateSha: $baseSha"
  } finally { Pop-Location }
  return $baseSha
}
function Change-ImpactFromEntries([string]$PreviousRelease, [string]$BaseSha, [string]$TargetSha, [object[]]$Entries, $Policy) {
  $allCategories = @()
  $unknown = @()
  $changed = foreach ($entry in @($Entries)) {
    [string[]]$paths = if ($entry.status[0] -in @('R','C')) { [string]$entry.oldPath; [string]$entry.newPath } else { [string]$entry.path }
    $pathCategories = [Collections.Generic.List[object]]::new()
    foreach ($path in $paths) { $pathCategories.Add(@(Path-Categories $path $Policy)) }
    $categories = @($pathCategories | ForEach-Object { @($_) })
    $regularModes = @('000000','100644','100755')
    $specialObject = $entry.status[0] -eq 'T' -or [string]$entry.oldMode -notin $regularModes -or [string]$entry.newMode -notin $regularModes
    if ($specialObject) { $categories += 'UNKNOWN'; $unknown += $paths }
    else {
      for ($pathIndex = 0; $pathIndex -lt $paths.Count; $pathIndex++) {
        if (@($pathCategories[$pathIndex]) -contains 'UNKNOWN') { $unknown += $paths[$pathIndex] }
      }
    }
    $categories = @(Sorted-Unique $categories)
    $allCategories += $categories
    if ($entry.status[0] -in @('R','C')) {
      [ordered]@{ status=[string]$entry.status; oldPath=[string]$entry.oldPath; newPath=[string]$entry.newPath; categories=$categories }
    } else {
      [ordered]@{ status=[string]$entry.status; path=[string]$entry.path; categories=$categories }
    }
  }
  $changed = @($changed | Sort-Object @{Expression={ if ($_.path) { $_.path } else { $_.newPath } }}, @{Expression={$_.oldPath}}, @{Expression={$_.status}} -CaseSensitive)
  $categories = @(Sorted-Unique $allCategories)
  $requiredEvidence = @()
  $reusableEvidence = @()
  foreach ($mapping in @($Policy.evidenceMappings)) {
    if ($categories -contains [string]$mapping.category) { $requiredEvidence += [string]$mapping.required }
    else { $reusableEvidence += [string]$mapping.reusable }
  }
  return [ordered]@{
    previousRelease=$PreviousRelease; baseProductSource=$BaseSha; candidateSha=$TargetSha
    changedFileCount=$changed.Count; changedFiles=$changed; categories=$categories; unknownFiles=@(Sorted-Unique $unknown)
    requiredEvidence=@(Sorted-Unique $requiredEvidence); reusableEvidence=@(Sorted-Unique $reusableEvidence)
    schemaChanged=$null; schemaEvidenceDisposition='NOT_EVALUATED'; schemaEvidenceReference=$null
  }
}
function Get-ChangeImpact([string]$Repository, [string]$PreviousRelease, [string]$TargetSha, $Policy) {
  $baseSha = Resolve-BaseProductSource $Repository $PreviousRelease $TargetSha
  return Change-ImpactFromEntries $PreviousRelease $baseSha $TargetSha @(Git-ChangeEntries $Repository $baseSha $TargetSha) $Policy
}
function Resolve-SetupCompatibility($Contract, [bool]$SchemaChanged, [string]$TargetVersion) {
  Require ($null -ne $Contract.PSObject.Properties['setupCompatibility']) 'release contract setupCompatibility is missing'
  $setup = $Contract.setupCompatibility
  Require ($null -ne $setup.PSObject.Properties['setupMode'] -and $null -ne $setup.PSObject.Properties['minimumDirectVersion'] -and
    $null -ne $setup.PSObject.Properties['crossSchemaAllowed']) 'setupCompatibility fields are incomplete'
  Require ($setup.crossSchemaAllowed -is [bool]) 'crossSchemaAllowed must be boolean'
  $setupMode = [string]$setup.setupMode
  $minimumDirectVersion = [string]$setup.minimumDirectVersion
  Require ($setupMode -in @('SAME_SCHEMA_SLIM','CROSS_SCHEMA_FULL')) "unsupported setupMode: $setupMode"
  Require ($minimumDirectVersion -match '^\d+\.\d+\.\d+$' -and ([Version]$minimumDirectVersion).ToString(3) -eq $minimumDirectVersion -and
    ([Version]$minimumDirectVersion -le [Version]$TargetVersion)) 'minimumDirectVersion must be an exact three-part version no newer than targetVersion'
  if ($setupMode -eq 'SAME_SCHEMA_SLIM') {
    Require (-not $SchemaChanged) 'SAME_SCHEMA_SLIM is forbidden when schemaChanged is true'
    Require (-not [bool]$setup.crossSchemaAllowed) 'SAME_SCHEMA_SLIM must set crossSchemaAllowed=false'
  } else {
    Require $SchemaChanged 'CROSS_SCHEMA_FULL requires schemaChanged=true'
    Require ([bool]$setup.crossSchemaAllowed) 'CROSS_SCHEMA_FULL must set crossSchemaAllowed=true'
    Require ($Contract.schemaEvidence.status -eq 'ACCEPTED' -and -not [string]::IsNullOrWhiteSpace([string]$Contract.schemaEvidence.reference)) 'CROSS_SCHEMA_FULL requires an accepted explicit schema compatibility contract'
  }
  return [ordered]@{ setupMode=$setupMode; minimumDirectSetupVersion=$minimumDirectVersion; embeddedUpdateAssets=($setupMode -eq 'CROSS_SCHEMA_FULL') }
}
function Include-PackageFile([string]$Relative) {
  if ($Relative.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { return $false }
  if ($Relative.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) -and -not $Relative.StartsWith('runtimes/win-x64/', [StringComparison]::OrdinalIgnoreCase)) { return $false }
  return $Relative -in @('StoreExpiryInspector.exe','createdump.exe','StoreExpiryInspector.dll','Updater/StoreExpiryInspector.Updater.exe','Updater/createdump.exe') -or
    $Relative.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
    $Relative.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
    $Relative.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)
}
function Package-Paths([string]$Root) {
  return @(Get-ChildItem -LiteralPath $Root -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/') } | Where-Object { Include-PackageFile $_ } | Sort-Object -CaseSensitive)
}
function Require-AdvanceComp([string]$Path, [string]$WorkingDirectory) {
  Require (Test-Path -LiteralPath $Path -PathType Leaf) 'AdvanceCOMP advzip is unavailable; configure STORE_EXPIRY_ADVZIP or -AdvanceComp'
  $version = (Native-AllText 'AdvanceCOMP version' $Path @('--version') $WorkingDirectory).Trim()
  Require ($version -match '^advancecomp v2\.6(?:\s|$)') "AdvanceCOMP v2.6 is required; received: $version"
  return $version.Split([Environment]::NewLine, [StringSplitOptions]::RemoveEmptyEntries)[0].Trim()
}
function Invoke-AdvanceComp([string]$Path, [string]$ZipPath, [string]$WorkingDirectory) {
  $version = Require-AdvanceComp $Path $WorkingDirectory
  $output = Native-AllText 'AdvanceCOMP advzip -4' $Path @('-z','-4','-k',$ZipPath) $WorkingDirectory
  Require (-not [string]::IsNullOrWhiteSpace($output) -and $output -match '(?m)^\s*\d+\s+\d+\s+\d+%') 'AdvanceCOMP returned unexpected output'
  return [ordered]@{ version=$version; output=$output }
}
function Write-Receipt {
  if (-not $receiptPath) { return }
  $required = @((Get-Content -Raw (Join-Path $PSScriptRoot 'release-receipt.schema.json') | ConvertFrom-Json).required)
  $missing = @($required | Where-Object { -not $receipt.Contains($_) })
  Require ($missing.Count -eq 0) "receipt fields missing: $($missing -join ', ')"
  [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
function Wait-StableFile([string]$Path, [int]$Seconds = 60) {
  $watch = [Diagnostics.Stopwatch]::StartNew(); $last = $null; $stable = 0
  while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
      $file = Get-Item -LiteralPath $Path; $identity = "$($file.Length):$($file.LastWriteTimeUtc.Ticks)"
      if ($identity -eq $last -and $file.Length -gt 0) { $stable++ } else { $stable = 0; $last = $identity }
      if ($stable -ge 2) { return $file }
    }
    Start-Sleep -Milliseconds 1000
  }
  throw "file did not become stable: $Path"
}
function Audit-Archive([string]$Path) {
  $archive = [IO.Compression.ZipFile]::OpenRead($Path)
  try {
    Require ($archive.Entries.Count -gt 0) 'archive is empty'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
      $name = $entry.FullName
      Require (-not $name.Contains('\')) "archive entry uses backslash: $name"
      Require (-not [IO.Path]::IsPathRooted($name) -and -not $name.Contains(':')) "archive entry is absolute or ADS: $name"
      Require (-not ($name.Split('/') | Where-Object { $_ -in @('', '.', '..') })) "archive entry traverses or is malformed: $name"
      Require ($seen.Add($name)) "archive duplicate or case collision: $name"
      Require (Include-PackageFile $name) "archive entry is outside allowlist: $name"
    }
    return [ordered]@{
      entryCount = $archive.Entries.Count
      pdbCount = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) }).Count
      nonWindowsRuntimeCount = @($archive.Entries | Where-Object { $_.FullName.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) -and -not $_.FullName.StartsWith('runtimes/win-x64/', [StringComparison]::OrdinalIgnoreCase) }).Count
    }
  } finally { $archive.Dispose() }
}
function Authenticode([string]$Path) { (Get-AuthenticodeSignature -LiteralPath $Path).Status.ToString() }

function Resolve-GenerationContract($Document, $Contract, $PreviousDocument, [string]$BuildMode) {
  $policy = $Document.compatibilityPolicy
  if ([Version]$Contract.targetVersion -le [Version]'1.1.3') { return $Contract }
  Require ($null -ne $policy) 'future releases require compatibility generation policy'
  Require ($policy.appliesAfterVersion -eq '1.1.3') 'generation adoption boundary must preserve published releases through 1.1.3'
  $id = if ($Contract.compatibilityGeneration) { [string]$Contract.compatibilityGeneration } else { [string]$policy.defaultGeneration }
  $matches = @($policy.generations | Where-Object { $_.id -eq $id })
  Require ($matches.Count -eq 1) 'release must resolve exactly one compatibility generation'
  Require (@($policy.generations.id | Select-Object -Unique).Count -eq @($policy.generations).Count) 'duplicate compatibility generation'
  Require (@($policy.generations | Where-Object { -not $_.previousGeneration }).Count -eq 1) 'only initial generation may omit predecessor; new generations require breaking-change/bridge evidence'
  $generation = $matches[0]
  $minimum = [string]$generation.minimumSourceVersion
  Require ($minimum -match '^\d+\.\d+\.\d+$' -and ([Version]$minimum).ToString(3) -eq $minimum) 'generation minimum must be an exact three-part version'
  Require ($Contract.previousRelease -match '^v\d+\.\d+\.\d+$') 'generation previousRelease must name a formal version tag'
  $maximum = $Contract.previousRelease.Substring(1)
  Require ([Version]$minimum -le [Version]$maximum -and [Version]$maximum -lt [Version]$Contract.targetVersion) 'generation source range or previousRelease is invalid'
  $migrations = @($generation.migrations)
  Require ($migrations.Count -gt 0 -and @($migrations | Select-Object -Unique).Count -eq $migrations.Count) 'generation requires full unique migration identity'
  Require (($migrations -join "`n") -ceq (($migrations | Sort-Object) -join "`n") -and @($migrations | Where-Object { $_ -notmatch '^\d{14}_[A-Za-z0-9_]+$' }).Count -eq 0) 'generation migration identity must contain ordered migration IDs'
  Require ($generation.breakingChange -is [bool]) 'generation requires explicit breakingChange boolean'
  Require ([int]$generation.minimumProtocolVersion -eq 2) 'generation requires existing protocol 2; protocol changes need separate runtime authorization'
  $previousPolicy = $PreviousDocument.compatibilityPolicy
  foreach ($historicalRelease in @($PreviousDocument.releases | Where-Object { [Version]$_.targetVersion -le [Version]'1.1.3' })) {
    $retainedRelease = @($Document.releases | Where-Object { $_.targetVersion -eq $historicalRelease.targetVersion })
    Require ($retainedRelease.Count -eq 1 -and ($historicalRelease | ConvertTo-Json -Depth 10 -Compress) -ceq ($retainedRelease[0] | ConvertTo-Json -Depth 10 -Compress)) 'published historical release contract changed'
  }
  if ($previousPolicy) {
    Require ($previousPolicy.appliesAfterVersion -eq $policy.appliesAfterVersion) 'generation adoption boundary changed'
    foreach ($old in $previousPolicy.generations) {
      $retained = @($policy.generations | Where-Object { $_.id -eq $old.id })
      Require ($retained.Count -eq 1) 'previous generation must be retained'
      foreach ($field in @('minimumSourceVersion','minimumProtocolVersion','migrations','breakingChange','previousGeneration','breakingChangeEvidence')) {
        Require (($old.$field | ConvertTo-Json -Depth 10 -Compress) -ceq ($retained[0].$field | ConvertTo-Json -Depth 10 -Compress)) "existing generation $($old.id) $field changed; create an evidenced new generation"
      }
    }
  }
  if (-not $generation.previousGeneration) {
    Require (-not $generation.breakingChange) 'initial generation cannot claim a breaking change'
    $historical = @($Document.releases | Where-Object { [Version]$_.targetVersion -le [Version]$policy.appliesAfterVersion -and $_.source.maxMigration -eq $migrations[-1] -and $_.setupCompatibility.setupMode -eq 'SAME_SCHEMA_SLIM' } | Sort-Object { [Version]$_.source.minVersion })
    Require ($historical.Count -gt 0 -and $minimum -eq $historical[0].source.minVersion) 'initial generation minimum must inherit earliest historical same-schema source'
  } else {
    $predecessor = @($policy.generations | Where-Object { $_.id -eq $generation.previousGeneration })
    Require ($predecessor.Count -eq 1 -and $predecessor[0].id -ne $id) 'new generation requires a distinct retained predecessor'
    Require ([Version]$minimum -gt [Version]$predecessor[0].minimumSourceVersion) 'new generation minimum must advance beyond its predecessor'
    Require ($generation.breakingChange -eq $true) 'new generation requires breaking-change evidence'
    $evidence = $generation.breakingChangeEvidence
    foreach ($field in @('reason','affectedVersions','bridge','userUpgradePath','whyPreviousGenerationCannotContinue','reference')) {
      Require (-not [string]::IsNullOrWhiteSpace([string]$evidence.$field)) "new generation requires breaking-change/bridge evidence: $field"
    }
    Require ($evidence.setupMinimum -eq $minimum -and $evidence.onlineMinimum -eq $minimum) 'new generation evidence must declare matching Setup and Online minima'
  }
  Require ($generation.minimumStatus -in @('CANDIDATE_NOT_VERIFIED','VERIFIED')) 'generation minimum status is required'
  if ($BuildMode -eq 'RELEASE_CANDIDATE') {
    Require ($generation.minimumStatus -eq 'VERIFIED' -and -not [string]::IsNullOrWhiteSpace([string]$generation.verificationEvidence)) 'public release candidate requires independently verified generation minimum evidence'
    foreach ($reference in @($generation.verificationEvidence, $generation.breakingChangeEvidence.reference) | Where-Object { $_ }) {
      Require ($reference -match '^\.ai-dev/' -and -not ($reference.Split('/') | Where-Object { $_ -eq '..' }) -and (Test-Path -LiteralPath (Join-Path $builderRoot $reference) -PathType Leaf)) 'release generation evidence must reference an existing governance file inside repository'
    }
  }
  if ($Contract.minimumProtocolVersion) { Require ($Contract.minimumProtocolVersion -eq $generation.minimumProtocolVersion) 'release protocol must inherit generation' }
  foreach ($field in @('minVersion','maxVersion','minMigration','maxMigration')) {
    $expected = switch ($field) { 'minVersion' { $minimum }; 'maxVersion' { $maximum }; default { $migrations[-1] } }
    if ($Contract.source -and $Contract.source.PSObject.Properties[$field]) { Require ($Contract.source.$field -eq $expected) "release source $field must inherit generation/previousRelease; narrowing is forbidden" }
  }
  if ($Contract.setupCompatibility.minimumDirectVersion) { Require ($Contract.setupCompatibility.minimumDirectVersion -eq $minimum) 'Setup minimumDirectVersion must inherit generation minimum' }
  Require ($Contract.setupCompatibility.setupMode -eq 'SAME_SCHEMA_SLIM' -and $Contract.setupCompatibility.crossSchemaAllowed -eq $false) 'generation resolver currently supports Same-Schema slim releases only'
  $resolved = $Contract | ConvertTo-Json -Depth 15 | ConvertFrom-Json
  $resolved | Add-Member -Force NoteProperty minimumProtocolVersion ([int]$generation.minimumProtocolVersion)
  $resolved | Add-Member -Force NoteProperty source ([pscustomobject]@{ minVersion=$minimum; maxVersion=$maximum; minMigration=$migrations[-1]; maxMigration=$migrations[-1] })
  $resolved.setupCompatibility | Add-Member -Force NoteProperty minimumDirectVersion $minimum
  $resolved | Add-Member -Force NoteProperty generationMigrations $migrations
  return $resolved
}

$changeImpactPolicy = Get-Content -Raw (Join-Path $PSScriptRoot 'change-impact-policy.json') | ConvertFrom-Json
if ($env:S25_RELEASE_GENERATION_PROBE -and $env:S25_RELEASE_GENERATION_INPUT) {
  try {
    $inputDocument = Get-Content -Raw $env:S25_RELEASE_GENERATION_INPUT | ConvertFrom-Json
    $resolved = Resolve-GenerationContract $inputDocument.document $inputDocument.contract $inputDocument.previousDocument ([string]$inputDocument.mode)
    $probeResult = @{ status='PASS'; contract=$resolved; failureReason=$null }
  } catch { $probeResult = @{ status='FAILED'; contract=$null; failureReason=$_.Exception.Message } }
  [IO.File]::WriteAllText($env:S25_RELEASE_GENERATION_PROBE, ($probeResult | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
  return
}
if ($env:S20_RELEASE_CHANGE_IMPACT_PROBE -and $env:S20_RELEASE_CHANGE_IMPACT_INPUT) {
  try {
    $probeInput = Get-Content -Raw $env:S20_RELEASE_CHANGE_IMPACT_INPUT | ConvertFrom-Json
    $impact = if ($probeInput.entries) {
      Change-ImpactFromEntries ([string]$probeInput.previousRelease) ([string]$probeInput.baseProductSource) ([string]$probeInput.candidateSha) @($probeInput.entries) $changeImpactPolicy
    } else {
      Get-ChangeImpact ([string]$probeInput.repository) ([string]$probeInput.previousRelease) ([string]$probeInput.candidateSha) $changeImpactPolicy
    }
    $probeResult = [ordered]@{ status=if (@($impact.unknownFiles).Count -eq 0) { 'PASS' } else { 'FAILED' }; failedGate=if (@($impact.unknownFiles).Count -eq 0) { $null } else { 'CHANGE_IMPACT' }; changeImpact=$impact; failureReason=if (@($impact.unknownFiles).Count -eq 0) { $null } else { 'one or more changed paths have UNKNOWN impact' } }
  } catch {
    $probeResult = [ordered]@{ status='FAILED'; failedGate='CHANGE_IMPACT'; changeImpact=$null; failureReason=$_.Exception.Message }
  }
  [IO.File]::WriteAllText($env:S20_RELEASE_CHANGE_IMPACT_PROBE, ($probeResult | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
  return
}
if ($env:S21_RELEASE_SETUP_MODE_PROBE -and $env:S21_RELEASE_SETUP_MODE_INPUT) {
  try {
    $probeInput = Get-Content -Raw $env:S21_RELEASE_SETUP_MODE_INPUT | ConvertFrom-Json
    $setup = Resolve-SetupCompatibility $probeInput.contract ([bool]$probeInput.schemaChanged) ([string]$probeInput.targetVersion)
    $probeResult = [ordered]@{ status='PASS'; failedGate=$null; setupCompatibility=$setup; failureReason=$null }
  } catch {
    $probeResult = [ordered]@{ status='FAILED'; failedGate='SETUP_COMPATIBILITY'; setupCompatibility=$null; failureReason=$_.Exception.Message }
  }
  [IO.File]::WriteAllText($env:S21_RELEASE_SETUP_MODE_PROBE, ($probeResult | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
  return
}
if ($env:S26_RELEASE_ZIP_PROBE -and $env:S26_RELEASE_ZIP_INPUT) {
  try {
    $probeInput = Get-Content -Raw $env:S26_RELEASE_ZIP_INPUT | ConvertFrom-Json
    $probePaths = [string[]](Package-Paths ([string]$probeInput.payloadRoot))
    if ($probeInput.create) {
      $probeArchive = [IO.Compression.ZipFile]::Open([string]$probeInput.zipPath, [IO.Compression.ZipArchiveMode]::Create)
      try { $probePaths | ForEach-Object { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($probeArchive, (Join-Path ([string]$probeInput.payloadRoot) $_), $_, [IO.Compression.CompressionLevel]::SmallestSize) | Out-Null } }
      finally { $probeArchive.Dispose() }
    }
    if ($probeInput.advanceComp) {
      $tool = Invoke-AdvanceComp ([string]$probeInput.advanceComp) ([string]$probeInput.zipPath) ([string]$probeInput.payloadRoot)
      $toolVersion = $tool.version
    } else { $toolVersion = $null }
    $result = [StoreExpiryInspector.ReleaseTools.ReleaseZipArchive]::VerifyAndNormalize([string]$probeInput.zipPath, [string]$probeInput.payloadRoot, $probePaths, [bool]$probeInput.normalize)
    if ($probeInput.maxBytes) { Require ($result.ZipBytes -lt [long]$probeInput.maxBytes) "ZIP must be smaller than $($probeInput.maxBytes) bytes; actual=$($result.ZipBytes)" }
    $probeResult = [ordered]@{ status='PASS'; failedGate=$null; toolVersion=$toolVersion; result=$result; failureReason=$null }
  } catch {
    $probeResult = [ordered]@{ status='FAILED'; failedGate='ZIP'; toolVersion=$null; result=$null; failureReason=$_.Exception.Message }
  }
  [IO.File]::WriteAllText($env:S26_RELEASE_ZIP_PROBE, ($probeResult | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
  return
}

$receipt = [ordered]@{
  schemaVersion = 4; runId = $runId; status = 'RUNNING'; mode = $Mode; version = $Version; candidateSha = $CandidateSha
  builderSha = $null; sourceClean = $null; builderSourceClean = $null; rid = 'win-x64'; selfContained = $true
  setupMode = $null; minimumDirectSetupVersion = $null; embeddedUpdateAssets = $null
  appVersion = $null; updaterVersion = $null; currentSchemaIdentity = $null; migrationCount = $null; latestMigration = $null
  pdbCount = $null; nonWindowsRuntimeCount = $null; zipEntryCount = $null; zipBytes = $null; zipSha256 = $null
  zipCompression = $null; advanceCompVersion = $null; zipHeaderNormalization = $null; archiveAudit = $null
  productionRevalidateForInstall = $null; productionExtractAuditedArchive = $null; signingAlgorithm = $null; signingFingerprint = $null; signatureVerified = $null
  authenticodeStatus = $null; isccExitCode = $null; assets = @(); changeImpact = $null
  startedAt = $startedAt; completedAt = $null; publishAuthorized = $false; failedGate = $null; failureReason = $null
}

try {
  $gate = 'OUTPUT_ROOT'
  $outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
  $builderPrefix = $builderRoot.TrimEnd('\') + '\'
  Require (-not $outputRootFull.StartsWith($builderPrefix, [StringComparison]::OrdinalIgnoreCase)) 'output root must be outside the builder repository'
  Require ([IO.Path]::GetPathRoot($outputRootFull) -ne $outputRootFull) 'output root cannot be a drive root'
  $run = Join-Path $outputRootFull $runId
  $source = Join-Path $run 'source'; $publish = Join-Path $run 'publish'; $assets = Join-Path $run 'assets'; $logs = Join-Path $run 'logs'
  New-Item -ItemType Directory -Force -Path $run,$assets,$logs | Out-Null
  $receiptPath = Join-Path $run 'release-receipt.json'
  Write-Receipt

  $gate = 'VERSION_IDENTITY'
  Require ($Version -match '^\d+\.\d+\.\d+$' -and ([Version]$Version).ToString(3) -eq $Version) 'Version must be an exact three-part version'

  $gate = 'CANDIDATE_SOURCE'
  Require ($CandidateSha -match '^[0-9a-f]{40}$') 'CandidateSha must be an exact 40-character commit SHA'

  $gate = 'BUILDER_SOURCE'
  $receipt.builderSha = (Native-Text 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'rev-parse','HEAD') $builderRoot).ToLowerInvariant()
  $receipt.builderSourceClean = [string]::IsNullOrWhiteSpace((Native-Text 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'status','--porcelain') $builderRoot))
  Require $receipt.builderSourceClean 'builder source must be clean'

  $gate = 'RELEASE_CONTRACT'
  $contractDocument = Get-Content -Raw (Join-Path $PSScriptRoot 'release-contract.json') | ConvertFrom-Json
  $contracts = @($contractDocument.releases | Where-Object { $_.targetVersion -eq $Version })
  Require ($contracts.Count -eq 1) 'input Version must have exactly one release contract'
  $contract = $contracts[0]
  $previousDocument = if ($contractDocument.compatibilityPolicy -and [Version]$Version -gt [Version]$contractDocument.compatibilityPolicy.appliesAfterVersion) {
    Native-CapturedText 'previous formal release contract' 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'show',"$($contract.previousRelease):tools/release/release-contract.json") $builderRoot | ConvertFrom-Json
  } else { $null }
  $contract = Resolve-GenerationContract $contractDocument $contract $previousDocument $Mode

  $gate = 'CANDIDATE_SOURCE'
  Native-Checked 'candidate commit lookup' 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'cat-file','-e',"$CandidateSha^{commit}") $builderRoot
  $gate = 'CHANGE_IMPACT'
  $receipt.changeImpact = Get-ChangeImpact $builderRoot ([string]$contract.previousRelease) $CandidateSha $changeImpactPolicy
  $receipt.changeImpact.schemaEvidenceReference = if ($contract.schemaEvidence) { [string]$contract.schemaEvidence.reference } else { $null }
  Write-Receipt
  Require (@($receipt.changeImpact.unknownFiles).Count -eq 0) 'one or more changed paths have UNKNOWN impact'

  $gate = 'CANDIDATE_SOURCE'
  Native-Checked 'candidate worktree creation' 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'worktree','add','--detach',$source,$CandidateSha) $builderRoot
  $actualSha = (Native-Text 'git' @('-c',"safe.directory=$source",'-C',$source,'rev-parse','HEAD') $source).ToLowerInvariant()
  Require ($actualSha -eq $CandidateSha) 'candidate worktree HEAD does not match CandidateSha'
  $receipt.sourceClean = [string]::IsNullOrWhiteSpace((Native-Text 'git' @('-c',"safe.directory=$source",'-C',$source,'status','--porcelain') $source))
  Require $receipt.sourceClean 'candidate source is not clean'
  Native-Checked 'candidate diff check' 'git' @('-c',"safe.directory=$source",'-C',$source,'diff','--check') $source

  $gate = 'VERSION_IDENTITY'
  $appProject = Join-Path $source 'src\StoreExpiryInspector\StoreExpiryInspector.csproj'
  $updaterProject = Join-Path $source 'src\StoreExpiryInspector.Updater\StoreExpiryInspector.Updater.csproj'
  $receipt.appVersion = Native-Text 'dotnet' @('msbuild',$appProject,'-nologo','-getProperty:Version') $source
  $receipt.updaterVersion = Native-Text 'dotnet' @('msbuild',$updaterProject,'-nologo','-getProperty:Version') $source
  Require ($receipt.appVersion -eq $Version -and $receipt.updaterVersion -eq $Version) 'candidate App/Updater Version does not match input Version'

  $gate = 'PUBLISH'
  New-Item -ItemType Directory -Force -Path $publish | Out-Null
  Native-Checked 'restore' 'dotnet' @('restore',(Join-Path $source 'StoreExpiryInspector.slnx'),'-p:NuGetAudit=false') $source
  Native-Checked 'win-x64 restore' 'dotnet' @('restore',$appProject,'-r','win-x64','-p:NuGetAudit=false') $source
  Native-Checked 'publish' 'dotnet' @('publish',$appProject,'-c','Release','--no-restore','-p:NuGetAudit=false','-p:PublishProfile=WinX64','-p:DebugType=None','-p:DebugSymbols=false','-o',$publish) $source
  Require (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'StoreExpiryInspector.dll'))).Version.ToString() -eq "$Version.0") 'published App assembly version mismatch'
  Require (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'Updater\StoreExpiryInspector.Updater.dll'))).Version.ToString() -eq "$Version.0") 'published Updater assembly version mismatch'

  $gate = 'SCHEMA_IDENTITY'
  $probe = Join-Path $run 'candidate-identity.json'
  $candidateSchemaAssembly = Join-Path $publish 'StoreExpiryInspector.UpdateSafety.dll'
  Require (Test-Path -LiteralPath $candidateSchemaAssembly -PathType Leaf) 'candidate schema identity assembly is missing'
  $env:S20_RELEASE_IDENTITY_PROBE = $probe
  $env:S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY = $candidateSchemaAssembly
  try { Native-Checked 'candidate identity probe' 'dotnet' @('test',(Join-Path $builderRoot 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'),'-c','Release','-p:NuGetAudit=false','--filter','FullyQualifiedName~ReleaseCandidateBuilderTests.CandidateIdentityProbeReadsCandidateAssembly','--logger','console;verbosity=minimal') $builderRoot }
  finally {
    Remove-Item Env:S20_RELEASE_IDENTITY_PROBE -ErrorAction SilentlyContinue
    Remove-Item Env:S20_RELEASE_CANDIDATE_SCHEMA_ASSEMBLY -ErrorAction SilentlyContinue
  }
  Require (Test-Path -LiteralPath $probe -PathType Leaf) 'candidate identity probe did not produce output'
  $identity = Get-Content -Raw $probe | ConvertFrom-Json
  $receipt.currentSchemaIdentity = @($identity.currentSchemaIdentity)
  $receipt.migrationCount = [int]$identity.migrationCount
  $receipt.latestMigration = [string]$identity.latestMigration
  if ($contract.generationMigrations) {
    Require (($contract.generationMigrations -join "`n") -ceq ($receipt.currentSchemaIdentity -join "`n")) 'candidate full schema identity does not match compatibility generation'
  }

  $gate = 'SCHEMA_GATE'
  $schemaChanged = $contract.source.maxMigration -ne $receipt.latestMigration
  $evidenceReference = if ($contract.schemaEvidence) { [string]$contract.schemaEvidence.reference } else { $null }
  if ($schemaChanged) {
    Require ($contract.schemaEvidence.status -eq 'ACCEPTED' -and (Test-Path -LiteralPath (Join-Path $source $evidenceReference) -PathType Leaf)) 'schema changed without a complete accepted disposition'
  }
  $receipt.changeImpact.schemaChanged = $schemaChanged
  $receipt.changeImpact.schemaEvidenceDisposition = if ($schemaChanged -and -not $contract.schemaEvidence) { 'NEW_SCHEMA_DISPOSITION_REQUIRED' } else { 'INHERIT_EXISTING_EVIDENCE' }
  $receipt.changeImpact.schemaEvidenceReference = $evidenceReference
  $setupCompatibility = Resolve-SetupCompatibility $contract $schemaChanged $Version
  $receipt.setupMode = $setupCompatibility.setupMode
  $receipt.minimumDirectSetupVersion = $setupCompatibility.minimumDirectSetupVersion
  $receipt.embeddedUpdateAssets = $setupCompatibility.embeddedUpdateAssets
  Write-Receipt

  $gate = 'ZIP'
  $zip = Join-Path $assets "StoreExpiryInspector-$Version-win-x64.zip"
  $packagePaths = [string[]](Package-Paths $publish)
  $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
  try {
    $packagePaths | ForEach-Object {
      [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $publish $_), $_, [IO.Compression.CompressionLevel]::SmallestSize) | Out-Null
    }
  } finally { $archive.Dispose() }
  $null = [StoreExpiryInspector.ReleaseTools.ReleaseZipArchive]::VerifyAndNormalize($zip, $publish, $packagePaths, $false)

  $gate = 'ADVANCECOMP'
  $advanceCompResult = Invoke-AdvanceComp $AdvanceComp $zip $run
  $receipt.advanceCompVersion = $advanceCompResult.version
  [IO.File]::WriteAllText((Join-Path $logs 'advzip.stdout.log'), $advanceCompResult.output, [Text.UTF8Encoding]::new($false))
  Require (Test-Path -LiteralPath $zip -PathType Leaf) 'AdvanceCOMP did not leave the ZIP output in place'

  $gate = 'ZIP_HEADER_NORMALIZATION'
  $zipResult = [StoreExpiryInspector.ReleaseTools.ReleaseZipArchive]::VerifyAndNormalize($zip, $publish, $packagePaths, $true)
  $receipt.zipEntryCount = $zipResult.EntryCount; $receipt.zipBytes = $zipResult.ZipBytes; $receipt.zipSha256 = $zipResult.ZipSha256
  $receipt.zipCompression = 'Deflate/.NET SmallestSize + AdvanceCOMP v2.6 advzip -4'
  $receipt.zipHeaderNormalization = "PASS; clearedLocalAndCentralHeaders=$($zipResult.ChangedHeaderCount); compressedDataSha256=$($zipResult.CompressedDataSha256)"
  Require ($receipt.zipBytes -lt 100000000) "ZIP must be smaller than 100000000 bytes; actual=$($receipt.zipBytes)"

  $gate = 'ARCHIVE_AUDIT'
  $audit = Audit-Archive $zip
  Require ($audit.entryCount -eq $receipt.zipEntryCount) 'archive entry count changed after ZIP verification'
  $receipt.pdbCount = $audit.pdbCount; $receipt.nonWindowsRuntimeCount = $audit.nonWindowsRuntimeCount
  Require ($receipt.pdbCount -eq 0 -and $receipt.nonWindowsRuntimeCount -eq 0) 'archive contains forbidden publish output'
  $receipt.archiveAudit = 'PASS'

  $gate = 'MANIFEST'
  $manifest = Join-Path $assets 'update-manifest.json'
  $manifestBody = [ordered]@{
    schemaVersion = 1; version = $Version; releaseTag = "v$Version"; repository = 'CodeVoyage3/xiaoqipaichanuanjian'; channel = 'stable'; rid = 'win-x64'
    minimumProtocolVersion = [int]$contract.minimumProtocolVersion
    package = [ordered]@{ fileName = (Split-Path $zip -Leaf); bytes = (Get-Item -LiteralPath $zip).Length; sha256 = (Hash $zip) }
    targetMigrations = @($receipt.currentSchemaIdentity)
    source = [ordered]@{ minVersion = $contract.source.minVersion; maxVersion = $contract.source.maxVersion; minMigration = $contract.source.minMigration; maxMigration = $contract.source.maxMigration }
  }
  [IO.File]::WriteAllText($manifest, ($manifestBody | ConvertTo-Json -Compress -Depth 8), [Text.UTF8Encoding]::new($false))

  $gate = 'SIGNING'
  Require (-not [string]::IsNullOrWhiteSpace($SigningKeyFile) -and (Test-Path -LiteralPath $SigningKeyFile -PathType Leaf)) 'production signing identity is unavailable; configure STORE_EXPIRY_SIGNING_KEY_FILE'
  $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $foreignAllow = @((Get-Acl -LiteralPath $SigningKeyFile).Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ([Security.Principal.NTAccount]$_.IdentityReference).Translate([Security.Principal.SecurityIdentifier]).Value -ne $currentSid })
  Require ($foreignAllow.Count -eq 0) 'production signing identity ACL is not exclusive'
  $privateKey = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($SigningKeyFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
  $rsa = [Security.Cryptography.RSA]::Create(); $rsa.ImportPkcs8PrivateKey($privateKey, [ref]0)
  Require ($rsa.KeySize -eq 3072) 'production signing identity must be RSA-3072'
  $receipt.signingAlgorithm = 'RSA-PSS/SHA256'
  $receipt.signingFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
  $signature = Join-Path $assets 'update-manifest.sig'
  [IO.File]::WriteAllBytes($signature, $rsa.SignData([IO.File]::ReadAllBytes($manifest), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss))

  $gate = 'SIGNATURE_VERIFY'
  $verify = [Security.Cryptography.RSA]::Create()
  try { $verify.ImportSubjectPublicKeyInfo($rsa.ExportSubjectPublicKeyInfo(), [ref]0); $receipt.signatureVerified = $verify.VerifyData([IO.File]::ReadAllBytes($manifest), [IO.File]::ReadAllBytes($signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss) }
  finally { $verify.Dispose() }
  Require $receipt.signatureVerified 'manifest RSA-PSS reverse verification failed'

  $gate = 'PRODUCTION_REVALIDATION'
  $revalidation = Join-Path $run 'production-revalidation.json'
  $env:S20_RELEASE_ASSET_DIR = $assets; $env:S20_RELEASE_VERSION = $Version; $env:S20_RELEASE_REVALIDATION_RESULT = $revalidation
  try { Native-Checked 'production RevalidateForInstall' 'dotnet' @('test',(Join-Path $builderRoot 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'),'-c','Release','-p:NuGetAudit=false','--filter','FullyQualifiedName~ReleaseCandidateBuilderTests.ProductionTrustAnchorRevalidatesReleaseCandidate','--logger','console;verbosity=minimal') $builderRoot }
  finally { 'S20_RELEASE_ASSET_DIR','S20_RELEASE_VERSION','S20_RELEASE_REVALIDATION_RESULT' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue } }
  Require (Test-Path -LiteralPath $revalidation -PathType Leaf) 'production revalidation did not produce output'
  $receipt.productionRevalidateForInstall = (Get-Content -Raw $revalidation | ConvertFrom-Json).outcome
  Require ($receipt.productionRevalidateForInstall -eq 'Verified') 'production RevalidateForInstall did not return Verified'
  $env:S26_RELEASE_ZIP_PATH = $zip; $env:S26_RELEASE_PUBLISH_DIR = $publish
  try { Native-Checked 'production ExtractAuditedArchive' 'dotnet' @('test',(Join-Path $builderRoot 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'),'-c','Release','-p:NuGetAudit=false','--filter','FullyQualifiedName~ReleaseCandidateBuilderTests.ProductionUpdaterExtractsReleaseCandidateWithoutChangingPayload','--logger','console;verbosity=minimal') $builderRoot }
  finally { 'S26_RELEASE_ZIP_PATH','S26_RELEASE_PUBLISH_DIR' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue } }
  $receipt.productionExtractAuditedArchive = 'PASS'

  $gate = 'ISCC'
  Require (Test-Path -LiteralPath $Compiler -PathType Leaf) 'ISCC is unavailable; configure STORE_EXPIRY_ISCC'
  $setup = Join-Path $assets "StoreExpiryInspector-Setup-$Version.exe"
  $stdout = Join-Path $logs 'iscc.stdout.log'; $stderr = Join-Path $logs 'iscc.stderr.log'; $processReceipt = Join-Path $logs 'iscc-process.json'
  $isccArguments = @("/DPayloadDir=`"$publish`"","/DOutputDir=`"$assets`"","/DAppVersion=$Version","/D$($receipt.setupMode)","/DMinimumDirectVersion=$($receipt.minimumDirectSetupVersion)")
  if ($receipt.embeddedUpdateAssets) { $isccArguments += @("/DUpdatePackage=`"$zip`"","/DUpdateManifest=`"$manifest`"","/DUpdateSignature=`"$signature`"") }
  $isccArguments += "`"$(Join-Path $source 'installer\StoreExpiryInspector.iss')`""
  $process = Start-Process -FilePath $Compiler -ArgumentList $isccArguments -WorkingDirectory $source -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
  [IO.File]::WriteAllText($processReceipt, ([ordered]@{ runId=$runId; pid=$process.Id; startedAt=$process.StartTime.ToUniversalTime().ToString('O'); executable=[IO.Path]::GetFullPath($Compiler); workingDirectory=$source; status='RUNNING' } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
  $process.WaitForExit(); $process.Refresh(); $receipt.isccExitCode = $process.ExitCode
  [IO.File]::WriteAllText($processReceipt, ([ordered]@{ runId=$runId; pid=$process.Id; startedAt=$process.StartTime.ToUniversalTime().ToString('O'); completedAt=[DateTimeOffset]::UtcNow.ToString('O'); executable=[IO.Path]::GetFullPath($Compiler); workingDirectory=$source; exitCode=$process.ExitCode; status='EXITED' } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
  Require ($receipt.isccExitCode -eq 0) "ISCC failed; stdout=$stdout stderr=$stderr"
  $null = Wait-StableFile $setup

  $gate = 'ASSET_FREEZE'
  $expected = @((Split-Path $zip -Leaf),(Split-Path $setup -Leaf),'update-manifest.json','update-manifest.sig')
  $actual = @(Get-ChildItem -LiteralPath $assets -File | Select-Object -ExpandProperty Name | Sort-Object)
  Require (@(Compare-Object ($expected | Sort-Object) $actual).Count -eq 0) 'asset directory does not contain exactly four release assets'
  $receipt.assets = @($expected | ForEach-Object { $file = Get-Item -LiteralPath (Join-Path $assets $_); [ordered]@{ fileName=$file.Name; bytes=$file.Length; sha256=(Hash $file.FullName) } })
  $receipt.authenticodeStatus = "App=$(Authenticode (Join-Path $publish 'StoreExpiryInspector.exe'));Updater=$(Authenticode (Join-Path $publish 'Updater\StoreExpiryInspector.Updater.exe'));Setup=$(Authenticode $setup)"
  $receipt.status = 'RELEASE_CANDIDATE_READY'; $receipt.completedAt = [DateTimeOffset]::UtcNow.ToString('O')
  Write-Receipt
  $run
}
catch {
  if ($receiptPath) {
    $receipt.status = 'FAILED'; $receipt.failedGate = $gate; $receipt.failureReason = $_.Exception.Message; $receipt.completedAt = [DateTimeOffset]::UtcNow.ToString('O'); $receipt.publishAuthorized = $false
    try { Write-Receipt } catch { }
  }
  throw
}
finally {
  if ($privateKey) { [Array]::Clear($privateKey, 0, $privateKey.Length) }
  if ($rsa) { $rsa.Dispose() }
}
