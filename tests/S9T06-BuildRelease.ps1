param(
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$SigningKeyFile
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'OutputRoot must be a new GUID directory.' }
$outputId = [guid]::Empty
if (-not [guid]::TryParse((Split-Path -Leaf $output), [ref]$outputId)) { throw 'OutputRoot must be a GUID directory.' }
if ([IO.Path]::GetFullPath($repo) -eq $output -or $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputRoot must be outside the repository.' }
$keyPath = [IO.Path]::GetFullPath($SigningKeyFile)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not (Test-Path -LiteralPath $keyPath) -or $keyPath.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $keyPath.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $keyPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Signing identity location is not approved.' }
for ($directory = Split-Path -Parent $keyPath; $directory; $directory = Split-Path -Parent $directory) { if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Signing identity location is not approved.' } }
if ((git -C $repo status --porcelain).Count -ne 0) { throw 'A production release requires a clean source checkout.' }
$commit = (git -C $repo rev-parse HEAD).Trim()
$sourceMinVersion = '1.0.0'
$migrations = @('20260826123739_InitialCreate','20260826130822_AddTasksAndDrafts','20260826135612_AddInspectionHistory','20260826142429_AddInventoryAdjustments','20260826152131_AddImportPersistence','20260826155455_AddBackupMetadata','20260826162033_AddSettingsAndAppState','20260826170403_AddLifecycleEvents','20260901155124_AddPolicyAndBaselineFoundation')
New-Item -ItemType Directory -Path $output | Out-Null
$publish = Join-Path $output 'publish'
$assets = Join-Path $output 'assets'
New-Item -ItemType Directory -Path $assets | Out-Null
dotnet publish (Join-Path $repo 'src\StoreExpiryInspector\StoreExpiryInspector.csproj') -c Release --no-restore -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'publish failed.' }
$app = Join-Path $publish 'StoreExpiryInspector.exe'
if (-not (Test-Path -LiteralPath $app) -or -not (Test-Path -LiteralPath (Join-Path $publish 'Updater\StoreExpiryInspector.Updater.exe'))) { throw 'Fresh publish is missing its application or independent Updater.' }
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion
$payloadVersion = [Version]::Parse($fileVersion)
if ($payloadVersion.Revision -ne 0) { throw 'Payload FileVersion is not a supported release version.' }
$version = $payloadVersion.ToString(3)
$zip = Join-Path $assets "StoreExpiryInspector-$version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    $unexpected = $names | Where-Object {
        $name = $_
        -not ($name -in @('StoreExpiryInspector.exe','createdump.exe','StoreExpiryInspector.dll','Updater/StoreExpiryInspector.Updater.exe','Updater/createdump.exe') -or $name.EndsWith('.dll') -or $name.EndsWith('.deps.json') -or $name.EndsWith('.runtimeconfig.json'))
    }
    if ($unexpected -or $names -notcontains 'StoreExpiryInspector.exe' -or $names -notcontains 'StoreExpiryInspector.dll' -or $names -notcontains 'Updater/StoreExpiryInspector.Updater.exe') { throw 'Fresh publish ZIP violates the frozen update-package allowlist.' }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestObject = [ordered]@{ schemaVersion = 1; version = $version; releaseTag = "v$version"; repository = 'CodeVoyage3/xiaoqipaichanuanjian'; channel = 'stable'; rid = 'win-x64'; minimumProtocolVersion = 1; package = [ordered]@{ fileName = [IO.Path]::GetFileName($zip); bytes = (Get-Item -LiteralPath $zip).Length; sha256 = $zipHash }; targetMigrations = $migrations; source = [ordered]@{ minVersion = $sourceMinVersion; maxVersion = $version; minMigration = $migrations[0]; maxMigration = $migrations[-1] } }
$manifest = Join-Path $assets 'update-manifest.json'
$manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifestObject | ConvertTo-Json -Depth 5 -Compress))
[IO.File]::WriteAllBytes($manifest, $manifestBytes)
$keyBytes = $null
$rsa = $null
try {
    $keyBytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $rsa = [Security.Cryptography.RSA]::Create()
    $read = 0
    $rsa.ImportPkcs8PrivateKey($keyBytes, [ref]$read)
    if ($read -ne $keyBytes.Length -or $rsa.KeySize -lt 3072) { throw 'Signing identity is invalid.' }
    $public = [Security.Cryptography.RSA]::Create()
    try { $public.ImportFromPem((Get-Content -Raw (Join-Path $repo '.ai-dev\ACCEPTANCE\S9-T06-PUBLIC-KEY.pem'))); if (([BitConverter]::ToString(([Security.Cryptography.SHA256]::Create()).ComputeHash($rsa.ExportSubjectPublicKeyInfo())) -replace '-','') -ne '565956021399C88A8B13DD0873D2A801F6675EAB44BEB4FC8EBE53C71FEFBADC' -or -not $public.ExportSubjectPublicKeyInfo().SequenceEqual($rsa.ExportSubjectPublicKeyInfo())) { throw 'Signing identity does not match the production trust anchor.' } } finally { $public.Dispose() }
    $signature = $rsa.SignData($manifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
    [IO.File]::WriteAllBytes((Join-Path $assets 'update-manifest.sig'), $signature)
    if (-not $rsa.VerifyData($manifestBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'Manifest signature verification failed.' }
}
finally { if ($rsa) { $rsa.Dispose() }; if ($keyBytes) { [Array]::Clear($keyBytes, 0, $keyBytes.Length) } }
& $Compiler "/DPayloadDir=$publish" "/DOutputDir=$assets" "/DAppVersion=$version" (Join-Path $repo 'installer\StoreExpiryInspector.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$setup = Join-Path $assets "StoreExpiryInspector-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw 'Installer output name is incorrect.' }
$tree = Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object { $relative = $_.FullName.Substring($publish.Length).TrimStart('\','/'); "$relative|$($_.Length)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
$treeHash = ([BitConverter]::ToString(([Security.Cryptography.SHA256]::Create()).ComputeHash([Text.Encoding]::UTF8.GetBytes(($tree -join "`n")))) -replace '-','')
$blocked = '-----BEGIN .*PRIVATE|PRIVATE KEY|(?i)(password|token|credential)\s*[:=]'
$scan = Get-ChildItem -LiteralPath $repo,$publish,$assets -File -Recurse | Where-Object { $_.Length -le 4MB } | Select-String -Pattern $blocked -List
if ($scan) { throw 'Secret scan found a prohibited marker.' }
$assetRows = Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
if (@($assetRows).Count -ne 4) { throw 'Release assets must contain exactly four files.' }
[ordered]@{ sourceCommit = $commit; version = $version; fileVersion = $fileVersion; migrationIds = $migrations; packageTreeSha256 = $treeHash; packageTree = $tree; assets = $assetRows } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-evidence.json') -Encoding utf8
Get-Content -Raw (Join-Path $output 'release-evidence.json')
