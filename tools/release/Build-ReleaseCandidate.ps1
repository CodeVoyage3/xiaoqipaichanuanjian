param(
  [Parameter(Mandatory)][string]$Version,
  [Parameter(Mandatory)][string]$CandidateSha,
  [ValidateSet('NOT_FOR_PUBLICATION','RELEASE_CANDIDATE')][string]$Mode = 'NOT_FOR_PUBLICATION',
  [string]$Compiler,
  [string]$SigningKeyFile,
  [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$builderRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$CandidateSha = $CandidateSha.ToLowerInvariant()
$Compiler = if ($Compiler) { $Compiler } elseif ($env:STORE_EXPIRY_ISCC) { $env:STORE_EXPIRY_ISCC } else { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
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
function Include-PackageFile([string]$Relative) {
  if ($Relative.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { return $false }
  if ($Relative.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase) -and -not $Relative.StartsWith('runtimes/win-x64/', [StringComparison]::OrdinalIgnoreCase)) { return $false }
  return $Relative -in @('StoreExpiryInspector.exe','createdump.exe','StoreExpiryInspector.dll','Updater/StoreExpiryInspector.Updater.exe','Updater/createdump.exe') -or
    $Relative.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
    $Relative.EndsWith('.deps.json', [StringComparison]::OrdinalIgnoreCase) -or
    $Relative.EndsWith('.runtimeconfig.json', [StringComparison]::OrdinalIgnoreCase)
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

$receipt = [ordered]@{
  schemaVersion = 1; runId = $runId; status = 'RUNNING'; mode = $Mode; version = $Version; candidateSha = $CandidateSha
  builderSha = $null; sourceClean = $null; builderSourceClean = $null; rid = 'win-x64'; selfContained = $true
  appVersion = $null; updaterVersion = $null; currentSchemaIdentity = $null; migrationCount = $null; latestMigration = $null
  pdbCount = $null; nonWindowsRuntimeCount = $null; zipEntryCount = $null; archiveAudit = $null
  productionRevalidateForInstall = $null; signingAlgorithm = $null; signingFingerprint = $null; signatureVerified = $null
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
  $contracts = @((Get-Content -Raw (Join-Path $PSScriptRoot 'release-contract.json') | ConvertFrom-Json).releases | Where-Object { $_.targetVersion -eq $Version })
  Require ($contracts.Count -eq 1) 'input Version must have exactly one release contract'
  $contract = $contracts[0]

  $gate = 'CANDIDATE_SOURCE'
  Native-Checked 'candidate commit lookup' 'git' @('-c',"safe.directory=$builderRoot",'-C',$builderRoot,'cat-file','-e',"$CandidateSha^{commit}") $builderRoot
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
  $env:S20_RELEASE_IDENTITY_PROBE = $probe
  try { Native-Checked 'candidate identity probe' 'dotnet' @('test',(Join-Path $source 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'),'-c','Release','--no-restore','-p:NuGetAudit=false','--filter','FullyQualifiedName~ReleaseCandidateBuilderTests.CandidateIdentityProbeUsesProductionSchemaAuthority','--logger','console;verbosity=minimal') $source }
  finally { Remove-Item Env:S20_RELEASE_IDENTITY_PROBE -ErrorAction SilentlyContinue }
  Require (Test-Path -LiteralPath $probe -PathType Leaf) 'candidate identity probe did not produce output'
  $identity = Get-Content -Raw $probe | ConvertFrom-Json
  $receipt.currentSchemaIdentity = @($identity.currentSchemaIdentity)
  $receipt.migrationCount = [int]$identity.migrationCount
  $receipt.latestMigration = [string]$identity.latestMigration

  $gate = 'SCHEMA_GATE'
  $schemaChanged = $contract.source.maxMigration -ne $receipt.latestMigration
  $evidenceReference = if ($contract.schemaEvidence) { [string]$contract.schemaEvidence.reference } else { $null }
  if ($schemaChanged) {
    Require ($contract.schemaEvidence.status -eq 'ACCEPTED' -and (Test-Path -LiteralPath (Join-Path $source $evidenceReference) -PathType Leaf)) 'schema changed without a complete accepted disposition'
  }
  $receipt.changeImpact = [ordered]@{ previousRelease=[string]$contract.previousRelease; schemaChanged=$schemaChanged; evidenceDisposition=if ($schemaChanged -and -not $contract.schemaEvidence) { 'NEW_SCHEMA_DISPOSITION_REQUIRED' } else { 'INHERIT_EXISTING_EVIDENCE' }; evidenceReference=$evidenceReference }

  $gate = 'ZIP'
  $zip = Join-Path $assets "StoreExpiryInspector-$Version-win-x64.zip"
  $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
  try {
    Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
      $relative = [IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/')
      if (Include-PackageFile $relative) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    }
  } finally { $archive.Dispose() }

  $gate = 'ARCHIVE_AUDIT'
  $audit = Audit-Archive $zip
  $receipt.zipEntryCount = $audit.entryCount; $receipt.pdbCount = $audit.pdbCount; $receipt.nonWindowsRuntimeCount = $audit.nonWindowsRuntimeCount
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
  try { Native-Checked 'production RevalidateForInstall' 'dotnet' @('test',(Join-Path $source 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj'),'-c','Release','--no-restore','-p:NuGetAudit=false','--filter','FullyQualifiedName~ReleaseCandidateBuilderTests.ProductionTrustAnchorRevalidatesReleaseCandidate','--logger','console;verbosity=minimal') $source }
  finally { 'S20_RELEASE_ASSET_DIR','S20_RELEASE_VERSION','S20_RELEASE_REVALIDATION_RESULT' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue } }
  Require (Test-Path -LiteralPath $revalidation -PathType Leaf) 'production revalidation did not produce output'
  $receipt.productionRevalidateForInstall = (Get-Content -Raw $revalidation | ConvertFrom-Json).outcome
  Require ($receipt.productionRevalidateForInstall -eq 'Verified') 'production RevalidateForInstall did not return Verified'

  $gate = 'ISCC'
  Require (Test-Path -LiteralPath $Compiler -PathType Leaf) 'ISCC is unavailable; configure STORE_EXPIRY_ISCC'
  $setup = Join-Path $assets "StoreExpiryInspector-Setup-$Version.exe"
  $stdout = Join-Path $logs 'iscc.stdout.log'; $stderr = Join-Path $logs 'iscc.stderr.log'; $processReceipt = Join-Path $logs 'iscc-process.json'
  $isccArguments = @("/DPayloadDir=`"$publish`"","/DOutputDir=`"$assets`"","/DAppVersion=$Version","/DUpdatePackage=`"$zip`"","/DUpdateManifest=`"$manifest`"","/DUpdateSignature=`"$signature`"","`"$(Join-Path $source 'installer\StoreExpiryInspector.iss')`"")
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
