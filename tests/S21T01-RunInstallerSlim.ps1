param(
  [Parameter(Mandatory)][string]$Compiler,
  [Parameter(Mandatory)][string]$ResultDirectory,
  [ValidateSet('CompileContract','Fresh','SameSchema','Repair','LegacyBlock','HigherBlock','UnsafeWalBlock','DowngradeBlock')]
  [string]$Scenario = 'CompileContract',
  [string]$CandidatePublish,
  [string]$V110Publish,
  [string]$V109Publish
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function New-GuidRoot { $path = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()); New-Item -ItemType Directory -Path $path | Out-Null; $path }
function Run([string]$File, [string[]]$Arguments) { $process = Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru; $process.ExitCode }

function Compile-Setup([string]$Publish, [string]$Version, [string]$Minimum, [string]$Mode, [string]$Install, [string]$Data, [string]$Output, [string]$Assets = '') {
  New-Item -ItemType Directory -Path $Output | Out-Null
  $id = [guid]::NewGuid().ToString()
  $arguments = @('/Qp','/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$Install","/DTestDataRoot=$Data","/DTestMutexName=$id","/DPayloadDir=$Publish","/DOutputDir=$Output","/DTestVersion=$Version","/DMinimumDirectVersion=$Minimum","/D$Mode")
  if ($Mode -eq 'CROSS_SCHEMA_FULL') {
    $arguments += @("/DUpdatePackage=$(Join-Path $Assets 'package.zip')","/DUpdateManifest=$(Join-Path $Assets 'update-manifest.json')","/DUpdateSignature=$(Join-Path $Assets 'update-manifest.sig')")
  }
  $log = Join-Path $Output 'iscc.log'
  $outputText = @(& $Compiler @arguments (Join-Path $root 'installer\StoreExpiryInspector.iss') 2>&1)
  $outputText | Set-Content -LiteralPath $log -Encoding utf8
  Require ($LASTEXITCODE -eq 0) "ISCC failed for $Mode $Version; log=$log"
  (Get-ChildItem -LiteralPath $Output -Filter '*Setup*.exe' | Select-Object -First 1).FullName
}

function Install([string]$Setup) { Run $Setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-') }
function Initialize-App([string]$Install, [string]$Data) {
  Run (Join-Path $Install 'app\StoreExpiryInspector.exe') @('--data-root', $Data, '--allow-existing-isolated-data-root', '--s9-t01-smoke-exit')
}
function Business([string]$Database, [string]$Mode, [string]$Evidence) {
  $env:S9_T07_E2E_DATABASE=$Database; $env:S9_T07_E2E_BUSINESS_MODE=$Mode; $env:S9_T07_E2E_BUSINESS_FINGERPRINT=$Evidence
  try {
    & dotnet test (Join-Path $root 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj') -c Release -p:NuGetAudit=false --filter 'FullyQualifiedName~V110InstallerE2EBusinessDataTests.SeedOrFingerprintIsLimitedToTheExplicitTemporaryInstallerDatabase' --logger 'console;verbosity=minimal'
    Require ($LASTEXITCODE -eq 0) "business probe failed: $Mode"
  } finally { 'S9_T07_E2E_DATABASE','S9_T07_E2E_BUSINESS_MODE','S9_T07_E2E_BUSINESS_FINGERPRINT' | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue } }
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

if ($Scenario -eq 'CompileContract') {
  $payload = Join-Path $result 'payload'; New-Item -ItemType Directory -Path $payload | Out-Null
  [IO.File]::WriteAllText((Join-Path $payload 'StoreExpiryInspector.exe'), 'compile-only payload')
  $install=New-GuidRoot; $data=New-GuidRoot
  $slim = Compile-Setup $payload '1.1.1' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'slim')
  $slimLog = Get-Content -Raw -LiteralPath (Join-Path $result 'slim\iscc.log')
  Require ($slimLog -notmatch 'package\.zip|update-manifest\.json|update-manifest\.sig') 'Slim compile consumed update assets'
  $missingOutput=Join-Path $result 'full-missing-assets'; New-Item -ItemType Directory -Path $missingOutput | Out-Null
  $id=[guid]::NewGuid().ToString(); $missing=@('/Qp','/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$install","/DTestDataRoot=$data","/DTestMutexName=$id","/DPayloadDir=$payload","/DOutputDir=$missingOutput",'/DTestVersion=1.1.0','/DMinimumDirectVersion=1.0.9','/DCROSS_SCHEMA_FULL')
  $null=@(& $Compiler @missing (Join-Path $root 'installer\StoreExpiryInspector.iss') 2>&1); Require ($LASTEXITCODE -ne 0) 'Full compile unexpectedly accepted missing update assets'
  $assets=Join-Path $result 'dummy-assets'; New-Item -ItemType Directory -Path $assets | Out-Null
  'zip','manifest','signature' | ForEach-Object -Begin {$names=@('package.zip','update-manifest.json','update-manifest.sig');$i=0} -Process {[IO.File]::WriteAllText((Join-Path $assets $names[$i++]),$_)}
  $full = Compile-Setup $payload '1.1.0' '1.0.9' 'CROSS_SCHEMA_FULL' $install $data (Join-Path $result 'full') $assets
  $slimBytes=(Get-Item -LiteralPath $slim).Length; $fullBytes=(Get-Item -LiteralPath $full).Length
  Require ($fullBytes -gt $slimBytes) 'Full Setup did not grow after embedding the three update assets'
  [IO.File]::WriteAllText((Join-Path $result 'result.json'),([ordered]@{scenario=$Scenario;slimSetup=$slim;slimBytes=$slimBytes;slimEmbeddedUpdateAssets=$false;fullMissingAssetsBlocked=$true;fullSetup=$full;fullBytes=$fullBytes;fullEmbeddedUpdateAssets=$true;status='PASS'}|ConvertTo-Json))
  return
}

Require (Test-Path -LiteralPath (Join-Path $CandidatePublish 'StoreExpiryInspector.exe') -PathType Leaf) 'CandidatePublish is required'
$install=New-GuidRoot; $data=New-GuidRoot; $database=Join-Path $data 'data\app.db'; $assets=Join-Path $result 'unused-assets'
$candidate = Compile-Setup $CandidatePublish '1.1.1' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'candidate')
$summary=[ordered]@{scenario=$Scenario;install=$install;data=$data;candidateSetup=$candidate;status='FAILED'}

if ($Scenario -eq 'Fresh') {
  $summary.setupExit=Install $candidate; Require ($summary.setupExit -eq 0) 'fresh install failed'
  Require ((Initialize-App $install $data) -eq 0) 'fresh app initialization failed'
  $summary.validation=Business $database 'validate' (Join-Path $result 'validation.json')
}
elseif ($Scenario -in @('SameSchema','Repair','HigherBlock','UnsafeWalBlock')) {
  Require (Test-Path -LiteralPath (Join-Path $V110Publish 'StoreExpiryInspector.exe') -PathType Leaf) 'V110Publish is required'
  $previous=Compile-Setup $V110Publish '1.1.0' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'previous')
  Require ((Install $previous) -eq 0 -and (Initialize-App $install $data) -eq 0) 'v1.1.0 baseline failed'
  if ($Scenario -eq 'HigherBlock') { Add-HigherMigration $database }
  elseif ($Scenario -eq 'UnsafeWalBlock') { New-Item -ItemType Directory -Path ($database + '-wal') | Out-Null }
  else {
    $null=Business $database 'seed' (Join-Path $result 'seed.txt')
    $summary.before=Business $database 'fingerprint' (Join-Path $result 'before.txt')
  }
  $summary.setupExit=Install $candidate
  if ($Scenario -in @('HigherBlock','UnsafeWalBlock')) { Require ($summary.setupExit -ne 0) "$Scenario unexpectedly succeeded" }
  else {
    Require ($summary.setupExit -eq 0) "$Scenario install failed"
    $summary.after=Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.after -eq $summary.before) "$Scenario changed business data"
    if ($Scenario -eq 'Repair') { $summary.repairExit=Install $candidate; Require ($summary.repairExit -eq 0) 'repair failed' }
    $summary.validation=Business $database 'validate' (Join-Path $result 'validation.json')
  }
}
elseif ($Scenario -eq 'LegacyBlock') {
  Require (Test-Path -LiteralPath (Join-Path $V109Publish 'StoreExpiryInspector.exe') -PathType Leaf) 'V109Publish is required'
  $legacy=Compile-Setup $V109Publish '1.0.9' '1.0.9' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'legacy')
  Require ((Install $legacy) -eq 0 -and (Initialize-App $install $data) -eq 0) 'v1.0.9 baseline failed'
  $null=Business $database 'seed' (Join-Path $result 'seed.txt'); $summary.before=Business $database 'fingerprint' (Join-Path $result 'before.txt'); $app=Join-Path $install 'app\StoreExpiryInspector.exe'; $appHash=Hash $app
  $summary.setupExit=Install $candidate; Require ($summary.setupExit -ne 0) 'legacy direct upgrade unexpectedly succeeded'
  Require ((Hash $app) -eq $appHash) 'legacy install tree changed'; $summary.after=Business $database 'fingerprint' (Join-Path $result 'after.txt'); Require ($summary.after -eq $summary.before) 'legacy database changed'
}
elseif ($Scenario -eq 'DowngradeBlock') {
  $newer=Compile-Setup $CandidatePublish '1.1.2' '1.1.0' 'SAME_SCHEMA_SLIM' $install $data (Join-Path $result 'newer')
  Require ((Install $newer) -eq 0) 'newer baseline failed'; $app=Join-Path $install 'app\StoreExpiryInspector.exe'; $appHash=Hash $app
  $summary.setupExit=Install $candidate; Require ($summary.setupExit -ne 0) 'downgrade unexpectedly succeeded'; Require ((Hash $app) -eq $appHash) 'downgrade changed install tree'
}

Assert-NoUpdaterTransaction $data
$summary.status='PASS'; [IO.File]::WriteAllText((Join-Path $result 'result.json'),($summary|ConvertTo-Json -Depth 8))
