param(
  [Parameter(Mandatory)][string]$Compiler,
  [Parameter(Mandatory)][string]$ResultDirectory,
  [ValidateSet('HarnessProbe','EvidenceReplay','CompileContract','Fresh','SameSchema','Repair','LegacyBlock','HigherBlock','UnsafeWalBlock','DowngradeBlock')]
  [string]$Scenario = 'CompileContract',
  [string]$CandidatePublish,
  [string]$V110Publish,
  [string]$V109Publish,
  [string]$V112Publish,
  [ValidateSet('ChineseConsole','MachineReadable','NonZero','Missing','Malformed')]
  [string]$HarnessProbeCase = 'MachineReadable',
  [string]$ExistingInstallRoot,
  [string]$ExistingDataRoot,
  [string]$ExistingIdentity
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Tree-Fingerprint([string]$Path) {
  $entries = @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object { "$( [IO.Path]::GetRelativePath($Path, $_.FullName) )|$(Hash $_.FullName)" })
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($entries -join "`n")))
}
function New-GuidRoot { $path = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()); New-Item -ItemType Directory -Path $path | Out-Null; $path }
function Run([string]$File, [string[]]$Arguments) { $process = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru; $process.ExitCode }

function Compile-Setup([string]$Publish, [string]$Version, [string]$Minimum, [string]$Mode, [string]$Install, [string]$Data, [string]$Output, [string]$Identity, [string]$Assets = '') {
  New-Item -ItemType Directory -Path $Output | Out-Null
  $arguments = @('/Qp','/DTestMode',"/DTestAppIdKey=$Identity","/DTestSuffix=$Identity","/DTestInstallRoot=$Install","/DTestDataRoot=$Data","/DTestMutexName=$Identity","/DPayloadDir=$Publish","/DOutputDir=$Output","/DTestVersion=$Version","/DMinimumDirectVersion=$Minimum","/D$Mode")
  if ($Mode -eq 'CROSS_SCHEMA_FULL') {
    $arguments += @("/DUpdatePackage=$(Join-Path $Assets 'package.zip')","/DUpdateManifest=$(Join-Path $Assets 'update-manifest.json')","/DUpdateSignature=$(Join-Path $Assets 'update-manifest.sig')")
  }
  $log = Join-Path $Output 'iscc.log'
  $outputText = @(& $Compiler @arguments (Join-Path $root 'installer\StoreExpiryInspector.iss') 2>&1)
  $outputText | Set-Content -LiteralPath $log -Encoding utf8
  Require ($LASTEXITCODE -eq 0) "ISCC failed for $Mode $Version; log=$log"
  (Get-ChildItem -LiteralPath $Output -Filter '*Setup*.exe' | Select-Object -First 1).FullName
}

function Install([string]$Setup, [string]$Log) { Run $Setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/LOG=$Log") }
function Initialize-App([string]$Install, [string]$Data) {
  Run (Join-Path $Install 'app\StoreExpiryInspector.exe') @('--data-root', $Data, '--allow-existing-isolated-data-root', '--s9-t01-smoke-exit')
}
function Assert-InstalledIdentity([string]$Install, [string]$Identity, [string]$Version) {
  $actualVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Install 'app\StoreExpiryInspector.dll')).Version.ToString(3)
  Require ($actualVersion -eq $Version) "installed App version mismatch: $actualVersion"
  $registration = Get-ItemProperty -LiteralPath "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$Identity}_is1"
  Require ($registration.DisplayVersion -eq $Version) 'test registration DisplayVersion mismatch'
  Require ([IO.Path]::GetFullPath([string]$registration.'Inno Setup: App Path') -eq [IO.Path]::GetFullPath($Install)) 'test registration install root mismatch'
}
function Read-ValidationResult([int]$ExitCode, [object[]]$ConsoleOutput, [string]$Evidence, [string]$Transcript) {
  $ConsoleOutput | Set-Content -LiteralPath $Transcript -Encoding utf8
  Require ($ExitCode -eq 0) "validation process failed: $ExitCode; transcript=$Transcript"
  Require (Test-Path -LiteralPath $Evidence -PathType Leaf) "validation JSON missing: $Evidence"
  try { Get-Content -Raw -LiteralPath $Evidence | ConvertFrom-Json -ErrorAction Stop }
  catch { throw "validation JSON malformed: $Evidence; $($_.Exception.Message)" }
}
function Business([string]$Database, [string]$Mode, [string]$Evidence) {
  $env:S9_T07_E2E_DATABASE=$Database; $env:S9_T07_E2E_BUSINESS_MODE=$Mode; $env:S9_T07_E2E_BUSINESS_FINGERPRINT=$Evidence
  try {
    $console = @(& dotnet test (Join-Path $root 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj') -c Release -p:NuGetAudit=false --filter 'FullyQualifiedName~V110InstallerE2EBusinessDataTests.SeedOrFingerprintIsLimitedToTheExplicitTemporaryInstallerDatabase' --logger 'console;verbosity=minimal' 2>&1)
    $exitCode = $LASTEXITCODE
  } finally { 'S9_T07_E2E_DATABASE','S9_T07_E2E_BUSINESS_MODE','S9_T07_E2E_BUSINESS_FINGERPRINT' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue } }
  $transcript = "$Evidence.transcript.log"
  if ($Mode -eq 'validate') { return (Read-ValidationResult $exitCode $console $Evidence $transcript) }
  $console | Set-Content -LiteralPath $transcript -Encoding utf8
  Require ($exitCode -eq 0) "business probe failed: $Mode; transcript=$transcript"
  Require (Test-Path -LiteralPath $Evidence -PathType Leaf) "business evidence missing: $Evidence"
  (Get-Content -Raw -LiteralPath $Evidence).Trim()
}
function Add-HigherMigration([string]$Database) {
  $env:S21_T01_DATABASE=$Database; $env:S21_T01_DATABASE_ACTION='ADD_HIGHER'
  try {
    & dotnet test (Join-Path $root 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj') -c Release -p:NuGetAudit=false --filter 'FullyQualifiedName~S21T01SlimSetupTests.DatabaseFixtureProbe' --logger 'console;verbosity=minimal'
    Require ($LASTEXITCODE -eq 0) 'higher-migration fixture probe failed'
  } finally { Remove-Item Env:S21_T01_DATABASE,Env:S21_T01_DATABASE_ACTION -ErrorAction SilentlyContinue }
}
function Assert-NoUpdaterTransaction([string]$Data) {
  $updates = Join-Path $Data 'updates'
  Require (-not (Test-Path -LiteralPath $updates) -or @(Get-ChildItem -LiteralPath $updates -Directory).Count -eq 0) 'unexpected updater transaction'
}

$result = [IO.Path]::GetFullPath($ResultDirectory)
Require (-not (Test-Path -LiteralPath $result)) 'ResultDirectory must be fresh'
$resultId = [guid]::Empty
Require ([guid]::TryParse((Split-Path -Leaf $result), [ref]$resultId)) 'ResultDirectory leaf must be a GUID'
New-Item -ItemType Directory -Path $result | Out-Null
$identity = [guid]::NewGuid().ToString()

if ($Scenario -eq 'HarnessProbe') {
  $evidence=Join-Path $result 'validation.json'; $transcript=Join-Path $result 'validation.transcript.log'
  if ($HarnessProbeCase -ne 'Missing') {
    $content=if($HarnessProbeCase -eq 'Malformed'){'{invalid'}else{'{"migrationCount":10,"integrity":"ok","foreignKeys":0}'}
    [IO.File]::WriteAllText($evidence,$content,[Text.UTF8Encoding]::new($false))
  }
  $exitCode=if($HarnessProbeCase -eq 'NonZero'){7}else{0}; $console=if($HarnessProbeCase -eq 'ChineseConsole'){@('正在运行验证','测试已通过')}else{@('validation transcript')}
  try { $parsed=Read-ValidationResult $exitCode $console $evidence $transcript; $probe=[ordered]@{case=$HarnessProbeCase;status='PASS';migrationCount=$parsed.migrationCount;reason=$null} }
  catch { $probe=[ordered]@{case=$HarnessProbeCase;status='FAILED';migrationCount=$null;reason=$_.Exception.Message} }
  [IO.File]::WriteAllText((Join-Path $result 'result.json'),($probe|ConvertTo-Json),[Text.UTF8Encoding]::new($false)); return
}

if ($Scenario -eq 'EvidenceReplay') {
  $install=[IO.Path]::GetFullPath($ExistingInstallRoot); $data=[IO.Path]::GetFullPath($ExistingDataRoot)
  $replayGuid=[guid]::Empty; Require ([guid]::TryParse([IO.Path]::GetRelativePath([IO.Path]::GetTempPath(),$install),[ref]$replayGuid)) 'replay install root must be a TEMP/GUID directory'
  $replayGuid=[guid]::Empty; Require ([guid]::TryParse([IO.Path]::GetRelativePath([IO.Path]::GetTempPath(),$data),[ref]$replayGuid)) 'replay data root must be a TEMP/GUID directory'
  $database=Join-Path $data 'data\app.db'; $beforeDb=Hash $database; $beforeTree=Tree-Fingerprint $install
  Assert-InstalledIdentity $install $ExistingIdentity '1.1.1'; Assert-NoUpdaterTransaction $data
  $validation=Business $database 'validate' (Join-Path $result 'validation.json')
  $afterDb=Hash $database; $afterTree=Tree-Fingerprint $install
  Require ($beforeDb -eq $afterDb) 'evidence replay changed database bytes'; Require ($beforeTree -eq $afterTree) 'evidence replay changed install tree'; Assert-NoUpdaterTransaction $data
  $replay=[ordered]@{scenario=$Scenario;status='PASS';productChain='ALREADY_COMPLETED';harnessEvidenceReplay='PASS';appVersion='1.1.1';beforeDbSha256=$beforeDb;afterDbSha256=$afterDb;beforeInstallTreeSha256=$beforeTree;afterInstallTreeSha256=$afterTree;updaterTransactions=0;validation=$validation}
  [IO.File]::WriteAllText((Join-Path $result 'result.json'),($replay|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false)); return
}

if ($Scenario -eq 'CompileContract') {
  $payload = Join-Path $result 'payload'; New-Item -ItemType Directory -Path $payload | Out-Null
  [IO.File]::WriteAllText((Join-Path $payload 'StoreExpiryInspector.exe'), 'compile-only payload')
  $install=New-GuidRoot; $data=New-GuidRoot
  $slim = Compile-Setup $payload '1.1.1' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'slim') $identity
  $slimLog = Get-Content -Raw -LiteralPath (Join-Path $result 'slim\iscc.log')
  Require ($slimLog -notmatch 'package\.zip|update-manifest\.json|update-manifest\.sig') 'Slim compile consumed update assets'
  $missingOutput=Join-Path $result 'full-missing-assets'; New-Item -ItemType Directory -Path $missingOutput | Out-Null
  $id=[guid]::NewGuid().ToString(); $missing=@('/Qp','/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$install","/DTestDataRoot=$data","/DTestMutexName=$id","/DPayloadDir=$payload","/DOutputDir=$missingOutput",'/DTestVersion=1.1.0','/DMinimumDirectVersion=1.0.9','/DCROSS_SCHEMA_FULL')
  $null=@(& $Compiler @missing (Join-Path $root 'installer\StoreExpiryInspector.iss') 2>&1); Require ($LASTEXITCODE -ne 0) 'Full compile unexpectedly accepted missing update assets'
  $assets=Join-Path $result 'dummy-assets'; New-Item -ItemType Directory -Path $assets | Out-Null
  'zip','manifest','signature' | ForEach-Object -Begin {$names=@('package.zip','update-manifest.json','update-manifest.sig');$i=0} -Process {[IO.File]::WriteAllText((Join-Path $assets $names[$i++]),$_)}
  $full = Compile-Setup $payload '1.1.0' '1.0.9' 'CROSS_SCHEMA_FULL' $install $data (Join-Path $result 'full') $identity $assets
  $slimBytes=(Get-Item -LiteralPath $slim).Length; $fullBytes=(Get-Item -LiteralPath $full).Length
  Require ($fullBytes -gt $slimBytes) 'Full Setup did not grow after embedding the three update assets'
  [IO.File]::WriteAllText((Join-Path $result 'result.json'),([ordered]@{scenario=$Scenario;slimSetup=$slim;slimBytes=$slimBytes;slimEmbeddedUpdateAssets=$false;fullMissingAssetsBlocked=$true;fullSetup=$full;fullBytes=$fullBytes;fullEmbeddedUpdateAssets=$true;status='PASS'}|ConvertTo-Json))
  return
}

Require (Test-Path -LiteralPath (Join-Path $CandidatePublish 'StoreExpiryInspector.exe') -PathType Leaf) 'CandidatePublish is required'
$install=New-GuidRoot; $data=New-GuidRoot; $database=Join-Path $data 'data\app.db'; $assets=Join-Path $result 'unused-assets'
$candidate = Compile-Setup $CandidatePublish '1.1.1' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'candidate') $identity
$candidateFile=Get-Item -LiteralPath $candidate
$summary=[ordered]@{scenario=$Scenario;identity=$identity;install=$install;data=$data;candidateSetup=$candidate;candidateSetupBytes=$candidateFile.Length;candidateSetupSha256=Hash $candidate;status='FAILED'}

if ($Scenario -eq 'Fresh') {
  $summary.setupExit=Install $candidate (Join-Path $result 'candidate-setup.log'); Require ($summary.setupExit -eq 0) 'fresh install failed'
  Assert-InstalledIdentity $install $identity '1.1.1'
  Require ((Initialize-App $install $data) -eq 0) 'fresh app initialization failed'
  $summary.validation=Business $database 'validate' (Join-Path $result 'validation.json'); Require ($summary.validation.migrationCount -eq 10) 'fresh migration count mismatch'; $summary.postinstallMutex='ISOLATED_SMOKE_PASS'
}
elseif ($Scenario -in @('SameSchema','Repair','HigherBlock','UnsafeWalBlock')) {
  Require (Test-Path -LiteralPath (Join-Path $V110Publish 'StoreExpiryInspector.exe') -PathType Leaf) 'V110Publish is required'
  $previous=Compile-Setup $V110Publish '1.1.0' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'previous') $identity
  Require ((Install $previous (Join-Path $result 'previous-setup.log')) -eq 0 -and (Initialize-App $install $data) -eq 0) 'v1.1.0 baseline failed'
  if ($Scenario -eq 'HigherBlock') { Add-HigherMigration $database }
  elseif ($Scenario -eq 'UnsafeWalBlock') { New-Item -ItemType Directory -Path ($database + '-wal') | Out-Null }
  else {
    $null=Business $database 'seed' (Join-Path $result 'seed.txt')
    $summary.before=Business $database 'fingerprint' (Join-Path $result 'before.txt')
  }
  $beforeDb=Hash $database; $beforeTree=Tree-Fingerprint $install; $summary.beforeDbSha256=$beforeDb; $summary.beforeInstallTreeSha256=$beforeTree
  $summary.setupExit=Install $candidate (Join-Path $result 'candidate-setup.log')
  if ($Scenario -in @('HigherBlock','UnsafeWalBlock')) {
    Require ($summary.setupExit -ne 0) "$Scenario unexpectedly succeeded"
    Require ((Hash $database) -eq $beforeDb -and (Tree-Fingerprint $install) -eq $beforeTree) "$Scenario changed protected state"
    $summary.afterDbSha256=Hash $database; $summary.afterInstallTreeSha256=Tree-Fingerprint $install
  }
  else {
    Require ($summary.setupExit -eq 0) "$Scenario install failed"
    Assert-InstalledIdentity $install $identity '1.1.1'
    Require ((Initialize-App $install $data) -eq 0) "$Scenario installed app smoke failed"; $summary.postinstallMutex='ISOLATED_SMOKE_PASS'
    Require ((Hash $database) -eq $beforeDb) "$Scenario changed database bytes"
    $summary.after=Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.after -eq $summary.before) "$Scenario changed business data"
    if ($Scenario -eq 'Repair') {
      $repairDb=Hash $database; $repairBusiness=$summary.after
      $summary.repairExit=Install $candidate (Join-Path $result 'repair-setup.log'); Require ($summary.repairExit -eq 0) 'repair failed'
      Assert-InstalledIdentity $install $identity '1.1.1'; Require ((Hash $database) -eq $repairDb) 'repair changed database bytes'
      $summary.repairBusiness=Business $database 'fingerprint' (Join-Path $result 'repair-after.txt'); Require ($summary.repairBusiness -eq $repairBusiness) 'repair changed business data'
    }
    $summary.validation=Business $database 'validate' (Join-Path $result 'validation.json'); Require ($summary.validation.migrationCount -eq 10) "$Scenario migration count mismatch"; $summary.afterDbSha256=Hash $database
  }
}
elseif ($Scenario -eq 'LegacyBlock') {
  Require (Test-Path -LiteralPath (Join-Path $V109Publish 'StoreExpiryInspector.exe') -PathType Leaf) 'V109Publish is required'
  $legacy=Compile-Setup $V109Publish '1.0.9' '1.0.9' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'legacy') $identity
  Require ((Install $legacy (Join-Path $result 'legacy-setup.log')) -eq 0 -and (Initialize-App $install $data) -eq 0) 'v1.0.9 baseline failed'
  Assert-InstalledIdentity $install $identity '1.0.9'; $null=Business $database 'seed' (Join-Path $result 'seed.txt'); $summary.before=Business $database 'fingerprint' (Join-Path $result 'before.txt'); $beforeDb=Hash $database; $beforeTree=Tree-Fingerprint $install; $summary.beforeDbSha256=$beforeDb; $summary.beforeInstallTreeSha256=$beforeTree
  $summary.setupExit=Install $candidate (Join-Path $result 'candidate-setup.log'); Require ($summary.setupExit -ne 0) 'legacy direct upgrade unexpectedly succeeded'
  Require ((Tree-Fingerprint $install) -eq $beforeTree -and (Hash $database) -eq $beforeDb) 'legacy protected state changed'; $summary.after=Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.after -eq $summary.before) 'legacy business data changed'
  $summary.afterDbSha256=Hash $database; $summary.afterInstallTreeSha256=Tree-Fingerprint $install
  $blockLog=Get-Content -Raw -LiteralPath (Join-Path $result 'candidate-setup.log'); Require ($blockLog.Contains('当前安装版本过旧') -and $blockLog.Contains('v1.1.0')) 'legacy bridge-upgrade prompt missing from Setup log'
}
elseif ($Scenario -eq 'DowngradeBlock') {
  Require (Test-Path -LiteralPath (Join-Path $V112Publish 'StoreExpiryInspector.exe') -PathType Leaf) 'V112Publish is required'
  $newer=Compile-Setup $V112Publish '1.1.2' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'newer') $identity
  Require ((Install $newer (Join-Path $result 'newer-setup.log')) -eq 0 -and (Initialize-App $install $data) -eq 0) 'newer baseline failed'; Assert-InstalledIdentity $install $identity '1.1.2'
  $null=Business $database 'seed' (Join-Path $result 'seed.txt'); $summary.before=Business $database 'fingerprint' (Join-Path $result 'before.txt'); $beforeDb=Hash $database; $beforeTree=Tree-Fingerprint $install; $summary.beforeDbSha256=$beforeDb; $summary.beforeInstallTreeSha256=$beforeTree
  $summary.setupExit=Install $candidate (Join-Path $result 'candidate-setup.log'); Require ($summary.setupExit -ne 0) 'downgrade unexpectedly succeeded'; Require ((Tree-Fingerprint $install) -eq $beforeTree -and (Hash $database) -eq $beforeDb) 'downgrade changed protected state'
  $summary.after=Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.after -eq $summary.before) 'downgrade changed business data'
  $summary.afterDbSha256=Hash $database; $summary.afterInstallTreeSha256=Tree-Fingerprint $install
}

Assert-NoUpdaterTransaction $data
$summary.status='PASS'; [IO.File]::WriteAllText((Join-Path $result 'result.json'),($summary|ConvertTo-Json -Depth 8))
