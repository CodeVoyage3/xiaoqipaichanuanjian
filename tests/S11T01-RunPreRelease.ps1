param(
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$result = [IO.Path]::GetFullPath($ResultDirectory)
$resultId = [guid]::Empty
if (Test-Path -LiteralPath $result) { throw 'fresh result directory required' }
if (-not [guid]::TryParse((Split-Path -Leaf $result), [ref]$resultId)) { throw 'result directory must be a direct GUID directory' }
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $result.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'result directory must be under TEMP' }

$runId = [guid]::NewGuid().ToString()
$run = Join-Path $temp $runId
$candidatePublish = Join-Path $run 'candidate-publish'
$sourcePublish = Join-Path $run 'source-publish'
$sourceInstall = Join-Path $temp ([guid]::NewGuid().ToString())
$sourceData = Join-Path $temp ([guid]::NewGuid().ToString())
$candidateInstall = Join-Path $temp ([guid]::NewGuid().ToString())
$candidateData = Join-Path $temp ([guid]::NewGuid().ToString())
$transport = Join-Path $run 'transport'
$evidence = Join-Path $run 'evidence'
New-Item -ItemType Directory -Path $result,$run,$transport,$evidence | Out-Null

function Invoke-Checked([string]$name, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$name failed: $LASTEXITCODE" }
}
function Sha([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Asset([string]$path) { [ordered]@{ name = Split-Path -Leaf $path; bytes = (Get-Item -LiteralPath $path).Length; sha256 = Sha $path } }
function Icon([string]$path) {
    $icon = [Drawing.Icon]::ExtractAssociatedIcon($path)
    if ($null -eq $icon) { throw "embedded icon missing: $path" }
    try {
        $stream = [IO.MemoryStream]::new()
        try { $icon.ToBitmap().Save($stream, [Drawing.Imaging.ImageFormat]::Png); [ordered]@{ path=$path; pngBytes=$stream.Length; pngSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream.ToArray())) } }
        finally { $stream.Dispose() }
    }
    finally { $icon.Dispose() }
}
function Validate-TestInstall([string]$install, [string]$suffix, [string]$setup) {
    $name = "StoreExpiryInspector S9-T02 $suffix"
    $exe = Join-Path $install 'app\StoreExpiryInspector.exe'
    $desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk"
    $startMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) "Programs\$name\$name.lnk"
    foreach ($shortcut in @($desktop,$startMenu)) {
        if (-not (Test-Path -LiteralPath $shortcut -PathType Leaf)) { throw "shortcut missing: $shortcut" }
        $shell = New-Object -ComObject WScript.Shell
        try { $target = $shell.CreateShortcut($shortcut).TargetPath }
        finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
        if ([IO.Path]::GetFullPath($target) -ne [IO.Path]::GetFullPath($exe)) { throw "shortcut target mismatch: $shortcut" }
    }
    $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$suffix}_is1"
    $entry = Get-ItemProperty -LiteralPath $key
    if ([IO.Path]::GetFullPath($entry.DisplayIcon) -ne [IO.Path]::GetFullPath($exe)) { throw 'uninstall display icon mismatch' }
    [ordered]@{ installedExe=(Icon $exe); setupExe=(Icon $setup); desktopShortcut=$desktop; startMenuShortcut=$startMenu; uninstallKey=$key; uninstallDisplayIcon=$entry.DisplayIcon }
}
function Install-Setup([string]$path) {
    $process = Start-Process -FilePath $path -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "setup install failed: $($process.ExitCode)" }
}
function Compile-Setup([string]$publish, [string]$output, [string]$version, [string]$install, [string]$data, [string]$suffix) {
    New-Item -ItemType Directory -Path $output | Out-Null
    $arguments = @('/Qp', "/DPayloadDir=$publish", "/DOutputDir=$output", '/DTestMode', "/DTestVersion=$version", "/DTestAppIdKey=$suffix", "/DTestSuffix=$suffix", "/DTestInstallRoot=$install", "/DTestDataRoot=$data", "/DTestMutexName=$suffix")
    & $Compiler @arguments (Join-Path $root 'installer\StoreExpiryInspector.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ISCC $version failed: $LASTEXITCODE" }
    (Get-ChildItem -LiteralPath $output -Filter '*Setup*.exe' | Select-Object -First 1).FullName
}

Push-Location $root
try {
    Invoke-Checked 'candidate production publish' {
        dotnet publish 'src\StoreExpiryInspector\StoreExpiryInspector.csproj' -c Release --no-restore -p:PublishProfile=WinX64 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $candidatePublish
    }
    $candidateExe = Join-Path $candidatePublish 'StoreExpiryInspector.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($candidateExe).FileVersion -ne '1.0.3.0') { throw 'candidate is not 1.0.3' }
    $forbidden = @('S11_PRE_RELEASE','--s9-t07-test-install','S9T07Fixture','20260905120000_S9T07Fixture10','.trx')
    foreach ($file in Get-ChildItem -LiteralPath $candidatePublish -File -Recurse) {
        if ($file.Name -match 'S9T07Fixture|\.trx$|\.db$') { throw "candidate forbidden file: $($file.Name)" }
        if ($file.Extension -in '.dll','.exe','.json','.config','.deps') {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $ascii = [Text.Encoding]::UTF8.GetString($bytes); $unicode = [Text.Encoding]::Unicode.GetString($bytes)
            foreach ($token in $forbidden) { if ($ascii.Contains($token) -or $unicode.Contains($token)) { throw "candidate forbidden token $token in $($file.Name)" } }
        }
    }

    $package = Join-Path $transport 'StoreExpiryInspector-1.0.3-win-x64.zip'
    Compress-Archive -Path (Join-Path $candidatePublish '*') -DestinationPath $package -CompressionLevel Optimal
    $migrations = @('20260826123739_InitialCreate','20260826130822_AddTasksAndDrafts','20260826135612_AddInspectionHistory','20260826142429_AddInventoryAdjustments','20260826152131_AddImportPersistence','20260826155455_AddBackupMetadata','20260826162033_AddSettingsAndAppState','20260826170403_AddLifecycleEvents','20260901155124_AddPolicyAndBaselineFoundation')
    $manifestPath = Join-Path $transport 'update-manifest.json'
    $manifest = [ordered]@{ schemaVersion=1; version='1.0.3'; releaseTag='v1.0.3'; repository='CodeVoyage3/xiaoqipaichanuanjian'; channel='stable'; rid='win-x64'; minimumProtocolVersion=1; package=[ordered]@{ fileName=(Split-Path -Leaf $package); bytes=(Get-Item $package).Length; sha256=(Sha $package).ToLowerInvariant() }; targetMigrations=$migrations; source=[ordered]@{ minVersion='1.0.2'; maxVersion='1.0.2'; minMigration=$migrations[0]; maxMigration=$migrations[-1] } }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        $signaturePath = Join-Path $transport 'update-manifest.sig'
        [IO.File]::WriteAllBytes($signaturePath, $rsa.SignData([IO.File]::ReadAllBytes($manifestPath), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss))
        $publicKey = [Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo())
        $publicHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo()))
        [IO.File]::WriteAllText((Join-Path $evidence 'public-spki.b64'), $publicKey, [Text.Encoding]::ASCII)

        Invoke-Checked 'source test-only publish' {
            dotnet publish 'src\StoreExpiryInspector\StoreExpiryInspector.csproj' -c Release --no-restore -p:PublishProfile=WinX64 -p:S9T07TestMode=true -p:Version=1.0.2 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o $sourcePublish
        }
        # The production Updater deliberately accepts only the formal LocalAppData root.
        # Rebuild the same independent Updater with its existing test-only TEMP-root gate;
        # no fault/checkpoint environment variables are set by this harness.
        Invoke-Checked 'source TEMP-root updater publish' {
            dotnet publish 'src\StoreExpiryInspector.Updater\StoreExpiryInspector.Updater.csproj' -c Release --no-restore -r win-x64 --self-contained true -p:S9T05TestMode=true -p:Version=1.0.2 -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false -o (Join-Path $sourcePublish 'Updater')
        }
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $sourcePublish 'StoreExpiryInspector.exe')).FileVersion -ne '1.0.2.0') { throw 'source host is not 1.0.2' }
        $sourceSetup = Compile-Setup $sourcePublish (Join-Path $run 'source-setup') '1.0.2' $sourceInstall $sourceData $runId
        Install-Setup $sourceSetup
        $candidateSuffix = [guid]::NewGuid().ToString()
        $candidateSetup = Compile-Setup $candidatePublish (Join-Path $run 'candidate-setup') '1.0.3' $candidateInstall $candidateData $candidateSuffix
        Install-Setup $candidateSetup
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $candidateInstall 'app\StoreExpiryInspector.exe')).FileVersion -ne '1.0.3.0') { throw 'candidate setup install version mismatch' }
        $installedSurface = Validate-TestInstall $candidateInstall $candidateSuffix $candidateSetup

        Invoke-Checked 'fresh test build' {
            dotnet build 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj' -c Release --no-restore --no-incremental -m:1 -p:S9T05TestMode=true -p:NuGetAudit=false
        }
        $env:S11_PRE_RELEASE_MODE = 'PRE_RELEASE_PRODUCTION_EQUIVALENT'
        $env:S11_PRE_RELEASE_INSTALL_ROOT = $sourceInstall
        $env:S11_PRE_RELEASE_DATA_ROOT = $sourceData
        $env:S11_PRE_RELEASE_EVIDENCE_ROOT = $evidence
        $env:S11_PRE_RELEASE_CANDIDATE_PUBLISH = $candidatePublish
        $env:S11_PRE_RELEASE_MANIFEST = $manifestPath
        $env:S11_PRE_RELEASE_SIGNATURE = $signaturePath
        $env:S11_PRE_RELEASE_PACKAGE = $package
        $env:S11_PRE_RELEASE_PUBLIC_KEY = $publicKey
        $env:S11_PRE_RELEASE_CACHE_ROOT = Join-Path $run 'cache'
        $trx = Join-Path $result 'S11-T01-pre-release.trx'
        Invoke-Checked 'pre-release runtime test' {
            dotnet test 'tests\StoreExpiryInspector.Tests\StoreExpiryInspector.Tests.csproj' -c Release --no-build --no-restore -p:NuGetAudit=false --filter 'FullyQualifiedName=StoreExpiryInspector.Tests.S11T01PreReleaseRuntimeTests.PreReleaseProductionEquivalentUpgradeAndRuntimeReset' --logger "trx;LogFileName=$(Split-Path -Leaf $trx)" --results-directory $result
        }
        $runtime = Get-Content (Join-Path $evidence 'runtime-result.json') -Raw | ConvertFrom-Json
        $final = [ordered]@{
            marker = 'PRE_RELEASE_PRODUCTION_EQUIVALENT'; officialGitHubBytes = $false; publicGitHubAssetsModified = $false
            disclosure = 'The 1.0.2 source host is a TEMP/GUID test-only build of the current production source and is not the official GitHub v1.0.2 binary. Its independent Updater is built from the same production source with only the existing test-root adapter enabled; it is not a formally released Updater binary, and no fault, checkpoint, fixture, state-machine, journal, rollback, or ACK bypass is enabled. Candidate bytes are normal production 1.0.3 publish bytes; only HTTP responses are substituted.'
            sourceVersion = '1.0.2'; targetVersion = '1.0.3'; migrationCount = $migrations.Count; runtime = $runtime
            signing = [ordered]@{ algorithm='RSA-PSS/SHA256'; rsaBits=3072; publicSpkiSha256=$publicHash; privateKeyRecorded=$false }
            assets = @((Asset $package),(Asset $manifestPath),(Asset $signaturePath),(Asset $candidateSetup))
            installedSurface = $installedSurface
            paths = [ordered]@{ runRoot=$run; upgradedGui=(Join-Path $sourceInstall 'app\StoreExpiryInspector.exe'); candidateSetup=$candidateSetup; evidence=$evidence }
        }
        [IO.File]::WriteAllText((Join-Path $result 'S11-T01-PRE-RELEASE-RESULT.json'), ($final | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    }
    finally { $rsa.Dispose() }
}
finally {
    Pop-Location
    Remove-Item Env:S11_PRE_RELEASE_MODE,Env:S11_PRE_RELEASE_INSTALL_ROOT,Env:S11_PRE_RELEASE_DATA_ROOT,Env:S11_PRE_RELEASE_EVIDENCE_ROOT,Env:S11_PRE_RELEASE_CANDIDATE_PUBLISH,Env:S11_PRE_RELEASE_MANIFEST,Env:S11_PRE_RELEASE_SIGNATURE,Env:S11_PRE_RELEASE_PACKAGE,Env:S11_PRE_RELEASE_PUBLIC_KEY,Env:S11_PRE_RELEASE_CACHE_ROOT -ErrorAction SilentlyContinue
}
