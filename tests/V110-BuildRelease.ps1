param(
  [Parameter(Mandatory)][string]$Compiler,
  [Parameter(Mandatory)][string]$OutputParent,
  [Parameter(Mandatory)][string]$SigningKeyFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = '1.1.0'; $sourceMigration = '20260901155124_AddPolicyAndBaselineFoundation'
$targetMigrations = @('20260826123739_InitialCreate','20260826130822_AddTasksAndDrafts','20260826135612_AddInspectionHistory','20260826142429_AddInventoryAdjustments','20260826152131_AddImportPersistence','20260826155455_AddBackupMetadata','20260826162033_AddSettingsAndAppState','20260826170403_AddLifecycleEvents',$sourceMigration,'20260912083448_AdjustCatchupWindowConstraint')
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Checked([string]$name, [scriptblock]$action) { & $action; if ($LASTEXITCODE) { throw "$name failed: $LASTEXITCODE" } }
function EntryVersion([string]$path) { ([Reflection.AssemblyName]::GetAssemblyName($path)).Version.ToString() }

Require (Test-Path -LiteralPath $Compiler -PathType Leaf) 'ISCC is unavailable'
Require (Test-Path -LiteralPath $SigningKeyFile -PathType Leaf) 'production signing identity is unavailable'
$outputParent = [IO.Path]::GetFullPath($OutputParent)
Require (-not $outputParent.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) 'delivery directory must be outside the repository'
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$foreignAllow = @((Get-Acl -LiteralPath $SigningKeyFile).Access | Where-Object {
  $_.AccessControlType -eq 'Allow' -and ([Security.Principal.NTAccount]$_.IdentityReference).Translate([Security.Principal.SecurityIdentifier]).Value -ne $currentSid
})
Require ($foreignAllow.Count -eq 0) 'production signing identity ACL is not exclusive'
$private = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($SigningKeyFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
$rsa = [Security.Cryptography.RSA]::Create()
try {
  $rsa.ImportPkcs8PrivateKey($private, [ref]0)
  Require ($rsa.KeySize -eq 3072) 'production signing identity must be RSA-3072'
  $spki = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo()))
  Require ($spki -eq '565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC') 'production signing identity SPKI mismatch'
  Checked 'git diff check' { git -c safe.directory=$root diff --check }
  $run = Join-Path $outputParent ([guid]::NewGuid().ToString())
  $publish = Join-Path $run 'publish'; $assets = Join-Path $run 'assets'
  New-Item -ItemType Directory -Force -Path $publish,$assets | Out-Null
  Checked 'publish' { dotnet publish "$root\src\StoreExpiryInspector\StoreExpiryInspector.csproj" -c Release --no-restore -p:NuGetAudit=false -o $publish }
  Require ((EntryVersion (Join-Path $publish 'StoreExpiryInspector.dll')) -eq '1.1.0.0') 'app version mismatch'
  Require ((EntryVersion (Join-Path $publish 'Updater\StoreExpiryInspector.Updater.dll')) -eq '1.1.0.0') 'updater version mismatch'
  $zip = Join-Path $assets 'StoreExpiryInspector-1.1.0-win-x64.zip'
  [IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
  $manifest = Join-Path $assets 'update-manifest.json'
  $content = [ordered]@{schemaVersion=1;version=$version;releaseTag='v1.1.0';repository='CodeVoyage3/xiaoqipaichanuanjian';channel='stable';rid='win-x64';minimumProtocolVersion=2;package=[ordered]@{fileName=(Split-Path $zip -Leaf);bytes=(Get-Item -LiteralPath $zip).Length;sha256=(Hash $zip)};targetMigrations=$targetMigrations;source=[ordered]@{minVersion='1.0.9';maxVersion='1.0.9';minMigration=$sourceMigration;maxMigration=$sourceMigration}}
  [IO.File]::WriteAllText($manifest, ($content | ConvertTo-Json -Compress -Depth 8), [Text.UTF8Encoding]::new($false))
  $signature = Join-Path $assets 'update-manifest.sig'
  [IO.File]::WriteAllBytes($signature, $rsa.SignData([IO.File]::ReadAllBytes($manifest), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss))
  $verify = [Security.Cryptography.RSA]::Create(); try { $verify.ImportSubjectPublicKeyInfo($rsa.ExportSubjectPublicKeyInfo(), [ref]0); Require ($verify.VerifyData([IO.File]::ReadAllBytes($manifest), [IO.File]::ReadAllBytes($signature), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) 'manifest RSA-PSS verification failed' } finally { $verify.Dispose() }
  Checked 'ISCC' { & $Compiler "/DPayloadDir=$publish" "/DOutputDir=$assets" "/DAppVersion=$version" "/DUpdatePackage=$zip" "/DUpdateManifest=$manifest" "/DUpdateSignature=$signature" "$root\installer\StoreExpiryInspector.iss" }
  $setup = Get-ChildItem -LiteralPath $assets -File -Filter '*Setup*.exe' | Select-Object -First 1
  Require ($null -ne $setup) 'Setup output missing'
  $head = (git -c safe.directory=$root rev-parse HEAD).Trim(); $diffHash = (git -c safe.directory=$root diff --binary | Out-String | %{ [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($_))).ToLowerInvariant() })
  $evidence = [ordered]@{version=$version;sourceVersion='1.0.9';sourceMigration=$sourceMigration;targetMigrationCount=$targetMigrations.Count;minimumProtocolVersion=2;sourceHead=$head;worktreeDiffSha256=$diffHash;productionSpkiSha256=$spki;assets=@(Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object { [ordered]@{name=$_.Name;bytes=$_.Length;sha256=(Hash $_.FullName)} })}
  [IO.File]::WriteAllText((Join-Path $run 'release-evidence.json'), ($evidence | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
  $run
} finally { [Array]::Clear($private,0,$private.Length); $rsa.Dispose() }
