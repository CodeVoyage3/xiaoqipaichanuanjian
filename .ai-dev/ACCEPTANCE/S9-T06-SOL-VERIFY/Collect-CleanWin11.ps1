param(
    [Parameter(Mandatory)][ValidateSet('Init','Before','After','Uninstalled')][string]$Phase,
    [switch]$SyntheticOnly,
    [string]$PacketRoot = $PSScriptRoot,
    [string]$SetupPath = (Join-Path $PacketRoot 'StoreExpiryInspector-Setup-1.0.0.exe')
)
$ErrorActionPreference = 'Stop'
if (-not $SyntheticOnly) { throw 'Only the independent synthetic test PC is authorized. Specify -SyntheticOnly.' }
$sha = [Security.Cryptography.SHA256]::Create()
try { $hostHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($env:COMPUTERNAME))).Replace('-','') }
finally { $sha.Dispose() }
if ($hostHash -eq '96EBD6A0749A0D1D0EF86E21E55E8E90E4E25CBE11B5736C369F17D2304FDC0F') {
    throw 'This is the development PC. No installed app or data paths were accessed.'
}
# This tool is run by the user on the separate clean PC. It never opens SQLite.
$data = Join-Path $env:LOCALAPPDATA 'StoreExpiryInspector'
$install = Join-Path $env:LOCALAPPDATA 'Programs\StoreExpiryInspector'
$marker = Join-Path $data '.s9t06-synthetic-only.json'
function Ordinary([string]$Path) {
    for ($item = [IO.DirectoryInfo][IO.Path]::GetFullPath($Path); $null -ne $item; $item = $item.Parent) {
        if ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked paths are not accepted.' }
    }
    if (Test-Path -LiteralPath $Path) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked evidence is not accepted.' }
        }
    }
}
function Inventory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    @(Get-ChildItem -LiteralPath $Path -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        [ordered]@{path=$_.FullName.Substring($Path.Length).TrimStart('\'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
function CopyEvidenceFile([string]$Source,[string]$Destination) {
    Ordinary $Source
    if ((Get-Item -LiteralPath $Source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked evidence file.' }
    $sourceStream = [IO.File]::Open($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        # Deny writers/deletion for this source handle during hashing and copying.
        Ordinary $Source
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $sourceHash = [BitConverter]::ToString($hash.ComputeHash($sourceStream)).Replace('-','') } finally { $hash.Dispose() }
        $sourceStream.Position = 0
        New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
        $output = [IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $sourceStream.CopyTo($output) } finally { $output.Dispose() }
        if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $sourceHash) { throw 'Copied evidence hash mismatch.' }
    } finally { $sourceStream.Dispose() }
}
if (Get-Process -Name StoreExpiryInspector,StoreExpiryInspector.Updater -ErrorAction SilentlyContinue) { throw 'Exit the app from its tray menu and wait for Updater to finish before collecting.' }
$PacketRoot = [IO.Path]::GetFullPath($PacketRoot).TrimEnd('\')
Ordinary $data; Ordinary $install; Ordinary $PacketRoot
if ($Phase -eq 'Init') {
    if ((Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $install)) { throw 'An app/data directory already exists. Stop; do not delete or reset it.' }
    if (-not (Test-Path -LiteralPath $SetupPath -PathType Leaf)) { throw 'Download the official 1.0.0 Setup into this folder first.' }
    if ((Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash -ne '12A30AAC034FA4B5D0A82DAF14B17E00437B13C0BC549AE45EECCA54E0675297') { throw 'Official 1.0.0 Setup SHA256 mismatch.' }
    $id = [guid]::NewGuid().ToString()
    $evidence = Join-Path $PacketRoot ('evidence-'+$id)
    New-Item -ItemType Directory -Path $evidence,$data | Out-Null
    [ordered]@{syntheticOnly=$true;runId=$id;evidence=$evidence;hostHash=$hostHash} | ConvertTo-Json | Set-Content -LiteralPath $marker -Encoding UTF8
} else {
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { throw 'Missing synthetic-only baseline marker. Stop; do not collect an existing business database.' }
    $run = Get-Content -Raw -LiteralPath $marker | ConvertFrom-Json
    if ($run.syntheticOnly -ne $true -or $run.hostHash -ne $hostHash) { throw 'Synthetic test identity mismatch.' }
    $id = [guid]::Parse($run.runId).ToString()
    $evidence = Join-Path $PacketRoot ('evidence-'+$id)
    if ($evidence -ne $run.evidence) { throw 'Use the original extracted test folder.' }
}
Ordinary $evidence
$snapshot = Join-Path $evidence $Phase
if (Test-Path -LiteralPath $snapshot) { throw 'This phase already has evidence; do not overwrite it.' }
New-Item -ItemType Directory -Path $snapshot | Out-Null
$os = Get-CimInstance Win32_OperatingSystem
$runtimes = @()
foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if ($base) {
        $shared = Join-Path $base 'dotnet\shared'
        if (Test-Path -LiteralPath $shared) { $runtimes += @(Get-ChildItem -LiteralPath $shared -Directory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory | Select-Object -ExpandProperty FullName }) }
    }
}
$app = Join-Path $install 'app\StoreExpiryInspector.exe'
$version = if (Test-Path -LiteralPath $app) { [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion } else { $null }
$appFiles = Inventory (Join-Path $install 'app')
$backupFiles = Inventory (Join-Path $data 'backups')
if ($Phase -ne 'Init') {
    $db = Join-Path $data 'data\app.db'
    if (-not (Test-Path -LiteralPath $db -PathType Leaf)) { throw 'The synthetic database is missing.' }
    foreach ($suffix in '-wal','-journal','-shm') { if (Test-Path -LiteralPath ($db+$suffix)) { throw 'Database sidecars remain. Do not delete them; stop and report.' } }
    CopyEvidenceFile $db (Join-Path $snapshot 'app.db')
    foreach ($name in 'backups','logs') {
        $source = Join-Path $data $name
        if (Test-Path -LiteralPath $source) {
            Ordinary $source
            foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) { CopyEvidenceFile $file.FullName (Join-Path (Join-Path $snapshot $name) $file.FullName.Substring($source.Length).TrimStart('\')) }
        }
    }
    $updates = Join-Path $data 'updates'
    if (Test-Path -LiteralPath $updates) {
        foreach ($operation in Get-ChildItem -LiteralPath $updates -Directory) {
            foreach ($name in 'journal.json','health-ack.json','manual-recovery.log') {
                $source = Join-Path $operation.FullName $name
                if (Test-Path -LiteralPath $source) {
                    $to = Join-Path $snapshot ('updates\'+$operation.Name)
                    New-Item -ItemType Directory -Path $to -Force | Out-Null
                    CopyEvidenceFile $source (Join-Path $to $name)
                }
            }
        }
    }
}
$registered = Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F90E64E-5B0D-4FA8-A854-EEA2F4D1EC14}_is1'
$appPresent = Test-Path -LiteralPath $app
if ($Phase -eq 'Uninstalled' -and ($registered -or $appPresent)) { throw 'App or uninstall registration still exists. Wait for uninstall to finish, then report; do not delete files manually.' }
if ($Phase -eq 'Before' -and $version -notmatch '^1\.0\.0(?:\+|$)') { throw 'Before evidence requires installed 1.0.0.' }
if ($Phase -eq 'After' -and $version -notmatch '^1\.0\.1(?:\+|$)') { throw 'After evidence requires installed 1.0.1.' }
$remainingProgramFiles = if ($Phase -eq 'Uninstalled') { Inventory $install } else { @() }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
[ordered]@{phase=$Phase;syntheticOnly=$true;capturedUtc=[DateTime]::UtcNow.ToString('O');os=$os.Caption;build=$os.Version;architecture=$os.OSArchitecture;processElevated=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);dotnetCommandPresent=[bool](Get-Command dotnet -ErrorAction SilentlyContinue);sharedRuntimes=$runtimes;productVersion=$version;appFiles=$appFiles;backupFiles=$backupFiles;databaseSha256=$(if($Phase -ne 'Init'){(Get-FileHash -LiteralPath (Join-Path $snapshot 'app.db') -Algorithm SHA256).Hash}else{$null})} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $snapshot 'evidence.json') -Encoding UTF8
[ordered]@{appExecutablePresent=$appPresent;uninstallRegistrationPresent=$registered;remainingProgramFiles=$remainingProgramFiles} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $snapshot 'installation-state.json') -Encoding UTF8
$zip = $evidence+'-'+$Phase+'.zip'
Compress-Archive -LiteralPath $evidence -DestinationPath $zip
Write-Output ('Evidence saved: '+$zip)
Write-Output 'Send the final Uninstalled evidence ZIP and GUI screenshots privately in this task. Never upload database/evidence to Release assets.'
