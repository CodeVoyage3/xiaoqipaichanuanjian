param([Parameter(Mandatory)][string]$Compiler,[ValidateSet('Success','Failure')][string]$Scenario='Success',[Parameter(Mandatory)][string]$LegacyPublish,[Parameter(Mandatory)][string]$ResultDirectory)

$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$id=[guid]::NewGuid().ToString(); $run=Join-Path ([IO.Path]::GetTempPath()) $id
$install=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString()); $data=Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$candidate=Join-Path $run 'candidate'; $testUpdater=Join-Path $run 'candidate-updater'; $assets=Join-Path $run 'assets'; $output=Join-Path $run 'setup'; $legacyOutput=Join-Path $run 'legacy-setup'; New-Item -ItemType Directory -Force -Path $run,$candidate,$testUpdater,$assets,$output,$legacyOutput,$ResultDirectory|Out-Null
function Hash([string]$p){
  $sha=[Security.Cryptography.SHA256]::Create()
  try { ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($p))).Replace('-','')).ToLowerInvariant() }
  finally { $sha.Dispose() }
}
function New-AuditableZip([string]$source,[string]$destination){
  $archive=[IO.Compression.ZipFile]::Open($destination,[IO.Compression.ZipArchiveMode]::Create)
  try {
    Get-ChildItem -LiteralPath $source -File -Recurse | ForEach-Object {
      $entry=[IO.Path]::GetRelativePath($source,$_.FullName).Replace('\','/')
      if ($entry -notmatch '^(StoreExpiryInspector\.exe|createdump\.exe|StoreExpiryInspector\.dll|Updater/StoreExpiryInspector\.Updater\.exe|Updater/createdump\.exe|.*\.dll|.*\.deps\.json|.*\.runtimeconfig\.json)$') { return }
      [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$_.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
  } finally { $archive.Dispose() }
}
function Checked([string]$n,[scriptblock]$b){& $b;if($LASTEXITCODE){throw "$n failed: $LASTEXITCODE"}}
function Require([bool]$condition,[string]$message){if(-not $condition){throw $message}}
function Wait-ExactProcess([Diagnostics.Process]$process,[int]$seconds,[string]$label){
  $until=[DateTime]::UtcNow.AddSeconds($seconds)
  while(-not $process.HasExited -and [DateTime]::UtcNow -lt $until){Start-Sleep -Milliseconds 200; $process.Refresh()}
  if(-not $process.HasExited){throw "$label did not exit within $seconds seconds"}
  return $process.ExitCode
}
function One-Operation([string]$root){
  $operations=@(Get-ChildItem -LiteralPath (Join-Path $root 'updates') -Directory -ErrorAction Stop)
  Require ($operations.Count -eq 1) "expected one update operation; actual=$($operations.Count)"
  return $operations[0]
}
function Assert-NoUpdateOperations([string]$root){
  $updates=Join-Path $root 'updates'
  if(Test-Path -LiteralPath $updates){ Require (@(Get-ChildItem -LiteralPath $updates -Directory).Count -eq 0) 'm10 repair created an updater transaction' }
}
function Write-BusinessFingerprint([string]$database,[string]$path,[ValidateSet('seed','fingerprint','validate')][string]$mode){
  $env:S9_T07_E2E_DATABASE=$database; $env:S9_T07_E2E_BUSINESS_FINGERPRINT=$path; $env:S9_T07_E2E_BUSINESS_MODE=$mode
  $transcript="$path.testhost.log"
  try { & dotnet test "$root\tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj" -c Release --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName~V110InstallerE2EBusinessDataTests' --logger 'console;verbosity=minimal' *>&1 | Tee-Object -FilePath $transcript | Out-Null; if($LASTEXITCODE){throw "business $mode failed: $LASTEXITCODE; transcript=$transcript"} }
  finally { Remove-Item Env:S9_T07_E2E_DATABASE -ErrorAction SilentlyContinue; Remove-Item Env:S9_T07_E2E_BUSINESS_FINGERPRINT -ErrorAction SilentlyContinue; Remove-Item Env:S9_T07_E2E_BUSINESS_MODE -ErrorAction SilentlyContinue }
  Require (Test-Path $path) "business $mode fingerprint evidence missing; transcript=$transcript"
  return (Get-Content -LiteralPath $path -Raw).Trim()
}
try {
  Checked 'candidate publish' { dotnet publish "$root\src\StoreExpiryInspector\StoreExpiryInspector.csproj" -c Release --no-restore -p:S9T07TestMode=true -p:NuGetAudit=false -o $candidate }
  # The production updater deliberately accepts only the formal LocalAppData root.
  # This isolated installer exercise uses its existing, compile-time-only TEMP-root
  # gate, matching the S11 harness without changing the production payload.
  Checked 'candidate TEMP-root updater publish' { dotnet publish "$root\src\StoreExpiryInspector.Updater\StoreExpiryInspector.Updater.csproj" -c Release --no-restore -r win-x64 --self-contained true -p:S9T05TestMode=true -p:NuGetAudit=false -o $testUpdater }
  # MSBuild's incremental copy can retain same-sized production files in the
  # host publish directory.  Explicitly overlay the separately compiled test
  # updater before signing the TestMode package.
  Copy-Item -Path (Join-Path $testUpdater '*') -Destination (Join-Path $candidate 'Updater') -Recurse -Force
  if ((Hash (Join-Path $candidate 'Updater\StoreExpiryInspector.Updater.dll')) -ne (Hash (Join-Path $testUpdater 'StoreExpiryInspector.Updater.dll'))) { throw 'candidate package did not receive the TEMP-root updater' }
  $zip=Join-Path $assets 'StoreExpiryInspector-1.1.0-win-x64.zip'; New-AuditableZip $candidate $zip
  $m=@('20260826123739_InitialCreate','20260826130822_AddTasksAndDrafts','20260826135612_AddInspectionHistory','20260826142429_AddInventoryAdjustments','20260826152131_AddImportPersistence','20260826155455_AddBackupMetadata','20260826162033_AddSettingsAndAppState','20260826170403_AddLifecycleEvents','20260901155124_AddPolicyAndBaselineFoundation','20260912083448_AdjustCatchupWindowConstraint')
  $rsa=[Security.Cryptography.RSA]::Create(3072); try {
    if($Scenario -eq 'Success'){
      # Current-schema repair only: no legacy package, updater transaction, or rollback path.
      $manifest=Join-Path $assets 'update-manifest.json'; $o=[ordered]@{schemaVersion=1;version='1.1.0';releaseTag='v1.1.0';repository='CodeVoyage3/xiaoqipaichanuanjian';channel='stable';rid='win-x64';minimumProtocolVersion=2;package=[ordered]@{fileName=(Split-Path $zip -Leaf);bytes=(gi $zip).Length;sha256=(Hash $zip)};targetMigrations=$m;source=[ordered]@{minVersion='1.0.9';maxVersion='1.0.9';minMigration=$m[0];maxMigration=$m[8]}}; [IO.File]::WriteAllText($manifest,($o|ConvertTo-Json -Compress -Depth 8),[Text.UTF8Encoding]::new($false)); $sig=Join-Path $assets 'update-manifest.sig'; [IO.File]::WriteAllBytes($sig,$rsa.SignData([IO.File]::ReadAllBytes($manifest),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss))
      $common=@('/Qp','/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$install","/DTestDataRoot=$data","/DTestMutexName=$id","/DUpdatePackage=$zip","/DUpdateManifest=$manifest","/DUpdateSignature=$sig")
      & $Compiler @common "/DPayloadDir=$candidate" "/DOutputDir=$output" '/DTestVersion=1.1.0' "$root\installer\StoreExpiryInspector.iss"; if($LASTEXITCODE){throw 'candidate ISCC failed'}
      $setup=(gi "$output\*Setup*.exe").FullName; $candidateLog=Join-Path $ResultDirectory 'candidate-setup.log'; $fresh=Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/LOG=$candidateLog") -Wait -PassThru
      Require ($fresh.ExitCode -eq 0) "fresh candidate Setup exit expected 0; actual=$($fresh.ExitCode)"
      $current=Start-Process (Join-Path $install 'app\StoreExpiryInspector.exe') -ArgumentList "--data-root `"$data`"",'--allow-existing-isolated-data-root','--s9-t01-smoke-exit' -Wait -PassThru
      Require ($current.ExitCode -eq 0) "current v1.1.0 m10 initialization failed: $($current.ExitCode)"
      $db=Join-Path $data 'data\app.db'; Require (Test-Path $db) 'database missing after current m10 initialization'; $beforeBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-before.txt') 'seed'; $before=Hash $db
      $null=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'm10-before-repair-validation.json') 'validate'; Require (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $install 'app\StoreExpiryInspector.dll')).Version.ToString(3)) -eq '1.1.0') 'current installed application version mismatch'; Assert-NoUpdateOperations $data
      $repairLog=Join-Path $ResultDirectory 'repair-setup.log'; $repair=Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/LOG=$repairLog") -Wait -PassThru
      Require ($repair.ExitCode -eq 0) "m10 repair Setup exit expected 0; actual=$($repair.ExitCode)"
      $repairBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-after-repair.txt') 'fingerprint'; Require ($repairBusiness -eq $beforeBusiness) 'm10 repair changed business-data fingerprint'
      $null=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'm10-after-repair-validation.json') 'validate'; Assert-NoUpdateOperations $data
      [IO.File]::WriteAllText((Join-Path $ResultDirectory 'v110-success.json'),(@{scenario='M10Repair';preDbSha256=$before;postDbSha256=(Hash $db);businessFingerprint=$beforeBusiness;freshSetupExit=$fresh.ExitCode;repairSetupExit=$repair.ExitCode;migrationCount=$m.Count;integrity='ok';foreignKeys='ok';updaterTransactions=0;status='TESTMODE_M10_REPAIR_COMPLETED'}|ConvertTo-Json))
      return
    }
    $manifest=Join-Path $assets 'update-manifest.json'; $o=[ordered]@{schemaVersion=1;version='1.1.0';releaseTag='v1.1.0';repository='CodeVoyage3/xiaoqipaichanuanjian';channel='stable';rid='win-x64';minimumProtocolVersion=2;package=[ordered]@{fileName=(Split-Path $zip -Leaf);bytes=(gi $zip).Length;sha256=(Hash $zip)};targetMigrations=$m;source=[ordered]@{minVersion='1.0.9';maxVersion='1.0.9';minMigration=$m[0];maxMigration=$m[8]}}; [IO.File]::WriteAllText($manifest,($o|ConvertTo-Json -Compress -Depth 8),[Text.UTF8Encoding]::new($false)); $sig=Join-Path $assets 'update-manifest.sig'; [IO.File]::WriteAllBytes($sig,$rsa.SignData([IO.File]::ReadAllBytes($manifest),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss))
    $common=@('/Qp','/DTestMode',"/DTestAppIdKey=$id","/DTestSuffix=$id","/DTestInstallRoot=$install","/DTestDataRoot=$data","/DTestMutexName=$id","/DUpdatePackage=$zip","/DUpdateManifest=$manifest","/DUpdateSignature=$sig"); & $Compiler @common "/DPayloadDir=$LegacyPublish" "/DOutputDir=$legacyOutput" '/DTestVersion=1.0.9' "$root\installer\StoreExpiryInspector.iss";if($LASTEXITCODE){throw 'legacy ISCC failed'}; $legacySetup=(gi "$legacyOutput\*Setup*.exe").FullName; $legacyLog=Join-Path $ResultDirectory 'legacy-setup.log'; $legacy=Start-Process $legacySetup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/LOG=`"$legacyLog`"") -Wait -PassThru;if($legacy.ExitCode){throw "legacy Setup failed: $($legacy.ExitCode); log=$legacyLog"}; $old=Start-Process (Join-Path $install 'app\StoreExpiryInspector.exe') -ArgumentList "--data-root `"$data`" --allow-existing-isolated-data-root --s9-t01-smoke-exit" -Wait -PassThru;if($old.ExitCode){throw 'legacy v1.0.9 initialization failed'}; $db=Join-Path $data 'data\app.db'; $beforeBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-before.txt') 'seed'; $before=Hash $db; & $Compiler @common "/DPayloadDir=$candidate" "/DOutputDir=$output" '/DTestVersion=1.1.0' "$root\installer\StoreExpiryInspector.iss";if($LASTEXITCODE){throw 'candidate ISCC failed'}
    $env:S9_T07_TEST_PUBLIC_KEY=[Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo()); $env:S9_T07_EXIT_AFTER_NORMAL_ACK='1'
    $sourceVerifiedMarker=Join-Path $ResultDirectory 'source-verified-logical-fingerprint.txt'
    if($Scenario -eq 'Failure'){$env:S9_T05_FAIL_PHASE='CandidateStaged';$env:S9_T07_SOURCE_VERIFIED_MARKER=$sourceVerifiedMarker}
    $setup=(gi "$output\*Setup*.exe").FullName; $candidateLog=Join-Path $ResultDirectory 'candidate-setup.log'
    # Do not let Start-Process -Wait inherit the intentionally started normal app.
    # The specific Setup process is the assertion boundary.
    $p=Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/LOG=`"$candidateLog`"") -PassThru
    $setupExit=Wait-ExactProcess $p 90 'candidate Setup'
    if($Scenario -eq 'Success'){
      Require ($setupExit -eq 0) "Setup success exit expected 0; actual=$setupExit"
      $current=Start-Process (Join-Path $install 'app\StoreExpiryInspector.exe') -ArgumentList "--data-root `"$data`"",'--allow-existing-isolated-data-root','--s9-t01-smoke-exit' -Wait -PassThru
      Require ($current.ExitCode -eq 0) "current v1.1.0 m10 initialization failed: $($current.ExitCode)"
      Require (Test-Path $db) 'database missing'; $afterBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-after.txt') 'fingerprint'; Require ($afterBusiness -eq $beforeBusiness) 'business-data fingerprint differs after current m10 initialization'
      $null=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'm10-before-repair-validation.json') 'validate'; Require (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $install 'app\StoreExpiryInspector.dll')).Version.ToString(3)) -eq '1.1.0') 'current installed application version mismatch'; Assert-NoUpdateOperations $data
      $repairLog=Join-Path $ResultDirectory 'repair-setup.log'
      $repair=Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/LOG=`"$repairLog`"") -Wait -PassThru
      Require ($repair.ExitCode -eq 0) "m10 repair Setup exit expected 0; actual=$($repair.ExitCode)"
      $repairBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-after-repair.txt') 'fingerprint'
      Require ($repairBusiness -eq $beforeBusiness) 'm10 repair changed business-data fingerprint'
      $null=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'm10-after-repair-validation.json') 'validate'; Assert-NoUpdateOperations $data
      [IO.File]::WriteAllText((Join-Path $ResultDirectory 'v110-success.json'),(@{scenario=$Scenario;preDbSha256=$before;postDbSha256=(Hash $db);businessFingerprint=$beforeBusiness;setupExit=$setupExit;repairSetupExit=$repair.ExitCode;migrationCount=$m.Count;integrity='ok';foreignKeys='ok';updaterTransactions=0;status='TESTMODE_M10_REPAIR_COMPLETED'}|ConvertTo-Json))
    } else {
      Require (Test-Path $db) 'database missing'; $afterBusiness=Write-BusinessFingerprint $db (Join-Path $ResultDirectory 'business-after.txt') 'fingerprint'; Require ($afterBusiness -eq $beforeBusiness) 'business-data fingerprint differs after installer transaction'
      $operation=One-Operation $data; $journalPath=Join-Path $operation.FullName 'journal.json'; Require (Test-Path $journalPath) 'cross-schema journal missing'; $journal=Get-Content -LiteralPath $journalPath -Raw|ConvertFrom-Json
      Require ($journal.OperationId -eq $operation.Name) 'journal operation identity mismatch'; Require ($journal.SourceVersion -eq '1.0.9' -and $journal.TargetVersion -eq '1.1.0') 'journal version identity mismatch'
      Require (@($journal.Schema.SourceMigrations).Count -eq 9 -and @($journal.Schema.TargetMigrations).Count -eq 10) 'journal migration identity mismatch'
      $ackPath=Join-Path $operation.FullName 'health-ack.json'; Require (Test-Path $ackPath) 'existing health acknowledgement missing'; $ack=Get-Content -LiteralPath $ackPath -Raw|ConvertFrom-Json
      Require ($ack.integrity -eq 'ok' -and $ack.foreignKeys -eq 'ok' -and $ack.coreRead -eq $true -and $ack.uiLoaded -eq $true) 'existing application verification acknowledgement is invalid'
      Require ($setupExit -ne 0) 'fault-injected Setup unexpectedly succeeded'
      Require ($journal.Phase -eq 15 -and $journal.Schema.Phase -eq 16) 'rolled-back terminal missing'
      Require ((Hash $db) -eq $before) 'rollback database SHA256 differs from the source database'
      Require (Test-Path $sourceVerifiedMarker) 'existing source logical-fingerprint verification marker missing'
      Require ((Get-Content -LiteralPath $sourceVerifiedMarker -Raw).Trim() -eq $journal.Schema.Snapshot.LogicalFingerprint) 'rollback logical fingerprint differs from the verified source snapshot'
      Require ($ack.version -eq '1.0.9' -and $ack.migrationCount -eq 9 -and $ack.lastMigration -eq '20260901155124_AddPolicyAndBaselineFoundation') 'old application acknowledgement identity mismatch'
      Require (([Reflection.AssemblyName]::GetAssemblyName((Join-Path $install 'app\StoreExpiryInspector.dll')).Version.ToString(3)) -eq '1.0.9') 'old app tree was not restored'
      [IO.File]::WriteAllText((Join-Path $ResultDirectory 'v110-failure.json'),(@{scenario=$Scenario;preDbSha256=$before;postDbSha256=(Hash $db);businessFingerprint=$beforeBusiness;setupExit=$setupExit;operationId=$operation.Name;journalPhase=$journal.Phase;schemaPhase=$journal.Schema.Phase;sourceLogicalFingerprint=(Get-Content -LiteralPath $sourceVerifiedMarker -Raw).Trim();snapshotLogicalFingerprint=$journal.Schema.Snapshot.LogicalFingerprint;integrity=$ack.integrity;foreignKeys=$ack.foreignKeys;oldAckVersion=$ack.version;status='TESTMODE_E2E_ROLLBACK_VERIFIED'}|ConvertTo-Json))
    }
  } finally {$rsa.Dispose()}
} catch { [IO.File]::WriteAllText((Join-Path $ResultDirectory 'harness-failure.txt'),($_ | Out-String)); throw
} finally {Remove-Item Env:S9_T07_TEST_PUBLIC_KEY -ErrorAction SilentlyContinue; Remove-Item Env:S9_T07_EXIT_AFTER_NORMAL_ACK -ErrorAction SilentlyContinue; Remove-Item Env:S9_T05_FAIL_PHASE -ErrorAction SilentlyContinue; Remove-Item Env:S9_T07_SOURCE_VERIFIED_MARKER -ErrorAction SilentlyContinue}
